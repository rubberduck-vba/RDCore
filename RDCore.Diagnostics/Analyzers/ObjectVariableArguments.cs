using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics;

namespace RDCore.Diagnostics.Analyzers;

// RD-VBAL §5.3.1.11; MS-VBAL §5.6.12, §5.3.1.11
// MS-VBA implements a public variable of a class as a property: an argument that names one (b.Count, Me.Count, .Count in With b) is what its Get returns, and
// what the procedure assigns to its parameter is lost, where the specification passes the variable. So is an element of the array a Variant variable of an
// object holds (b.V(0)). What each argument is, and what it is given to, is read off the tree and off the facts of the module's expressions and arguments.
internal sealed class ObjectVariableArguments
{
    private readonly Dictionary<SyntaxNodeId, ExpressionFactDto> _facts = [];
    private readonly Dictionary<SyntaxNodeId, ExpressionNode> _nodes = [];
    // a member access with no owner, in a With block: the expression of the innermost block it is in (MS-VBAL §5.6.15).
    private readonly Dictionary<SyntaxNodeId, ExpressionNode> _withTargets = [];
    private readonly List<(ExpressionNode Callee, IReadOnlyList<ExpressionNode> Arguments)> _calls = [];

    private ObjectVariableArguments(SyntaxNode module, ModuleSemanticsDto semantics)
    {
        foreach (var fact in semantics.Procedures.SelectMany(procedure => procedure.Expressions))
        {
            _facts.TryAdd(fact.Node, fact);
        }

        Arguments = [.. semantics.Procedures.SelectMany(procedure => procedure.Arguments ?? [])];
        Walk(module);
    }

    public IReadOnlyList<ArgumentFact> Arguments { get; }

    public static ObjectVariableArguments? Of(ModuleAnalysisContext context)
        => context.ParseResult.SyntaxTree is { } module && context.Semantics is { } semantics ? new ObjectVariableArguments(module, semantics) : null;

    // the arguments the static pass maps to a parameter that takes them by reference: a ByRef parameter, or one with no keyword, of a procedure of the
    // project, a Declare'd procedure or an event; and a ParamArray, which collects them by reference (recorded in Excel). A library's procedure is left out.
    public IEnumerable<SourceRange> CopiesGivenByRef()
        => Arguments
            .Where(argument => !argument.IsLibrary && (argument.IsParamArray || argument.Mechanism is ParameterKind.ImplicitByRef or ParameterKind.ExplicitByRef))
            .Select(argument => _nodes.TryGetValue(argument.Argument, out var passed) && IsObjectVariableOrElement(passed) ? passed.Location.Range : (SourceRange?)null)
            .OfType<SourceRange>()
            .Distinct();

    // every argument of a member of an object bound late (an Object, a Variant), or of the target of a With block that is one: whose parameters are not
    // known until it runs.
    public IEnumerable<SourceRange> CopiesGivenLate()
        => _calls
            .Where(call => IsCalledLate(call.Callee))
            .SelectMany(call => call.Arguments)
            .Select(argument => argument is NamedArgumentNode named ? named.Value : argument)
            .Where(IsObjectVariableOrElement)
            .Select(passed => passed.Location.Range)
            .Distinct();

    private void Walk(SyntaxNode module)
    {
        var pending = new Stack<(SyntaxNode Node, ExpressionNode? WithTarget)>();
        pending.Push((module, null));
        while (pending.TryPop(out var visit))
        {
            var (node, withTarget) = visit;
            switch (node)
            {
                case MemberAccessExpressionNode { Owner: null } access when withTarget is not null:
                    _withTargets.TryAdd(access.Identity, withTarget);
                    break;
                case IndexExpressionNode index:
                    _calls.Add((index.Callee, index.Arguments));
                    break;
                case CallStatementNode call:
                    _calls.Add((call.Callee, call.Arguments));
                    break;
            }

            if (node is ExpressionNode expression)
            {
                _nodes.TryAdd(expression.Identity, expression);
            }

            // the expression of a With block is relative to the block it is in, and what the block holds to that expression.
            foreach (var child in node.ChildNodes())
            {
                pending.Push((child, node is WithStatementNode with && !ReferenceEquals(child, with.WithExpression) ? with.WithExpression : withTarget));
            }
        }
    }

    private ExpressionFactDto? FactOf(ExpressionNode? node) => node is not null && _facts.TryGetValue(node.Identity, out var fact) ? fact : null;

    private ExpressionFactDto? OwnerFactOf(MemberAccessExpressionNode access)
        => FactOf(access.Owner ?? (_withTargets.TryGetValue(access.Identity, out var target) ? target : null));

    // b.Count: a variable named as a member of an object of a class, which is the class's public variable. A member of a module the owner names has no TypeKind,
    // and is the module's variable.
    private bool IsObjectVariable(MemberAccessExpressionNode access)
        => OwnerFactOf(access) is { TypeKind: DeclaredTypeKind.Class, IsArray: false } && FactOf(access) is { Classification: SDK.Semantics.ExpressionClassification.Variable };

    // b.V(0): an element of the array a Variant variable of an object holds, whose variable is the copy that is passed. Of a variable of any other type
    // (b.Items(1), Items a Collection) it is the value of the default member of what the variable holds, which MS-VBAL passes as a copy too.
    private bool IsObjectVariableOrElement(ExpressionNode passed) => passed switch
    {
        MemberAccessExpressionNode access => IsObjectVariable(access),
        IndexExpressionNode { Callee: MemberAccessExpressionNode access } index when !IndexExpressionNode.IsEmptyArgumentList(index.Arguments)
            => IsObjectVariable(access) && FactOf(access) is { TypeKind: DeclaredTypeKind.Variant, IsArray: false },
        _ => false,
    };

    private bool IsCalledLate(ExpressionNode callee)
        => callee is MemberAccessExpressionNode access && OwnerFactOf(access) is { TypeKind: DeclaredTypeKind.Object or DeclaredTypeKind.Variant, IsArray: false };
}
