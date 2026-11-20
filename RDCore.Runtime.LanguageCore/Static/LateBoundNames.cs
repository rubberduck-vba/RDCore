using System.Collections.Immutable;
using System.Text.RegularExpressions;
using RDCore.Runtime.LanguageCore.Refactoring;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Runtime.LanguageCore.Static;

// MS-VBAL §5.6.12: a member access whose owner is of no particular class (an Object, a Variant) is bound where the code runs, to the member of the object's
// class with the name the access gives: any class's member of that name can be what it calls. MS-VBAL §6.1.2.8.1.1: CallByName calls the member a string
// names, which the code need not write at all. Members is each name that a module's code accesses late, or that a CallByName names with a string literal;
// CallsByName, whether it calls CallByName with any other name.
public sealed record class LateBoundNames(IReadOnlySet<string> Members, bool CallsByName)
{
    private const string CallByName = "CallByName";

    private const string ProcName = "ProcName";

    private static readonly Regex Word = new(@"\p{L}[\p{L}\p{Nd}_]*", RegexOptions.CultureInvariant);

    // each name a string literal of the module writes ("Module1.Helper" writes Module1 and Helper): a host can call back a procedure by the name a string
    // gives it (Excel's Application.Run, OnAction, OnTime), and no expression names it.
    public IReadOnlySet<string> NamedInStrings { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public static LateBoundNames Of(SyntaxNode module, IEnumerable<ProcedureSemanticModel> procedures)
    {
        var models = procedures.ToList();
        var members = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var strings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var namedByLiteral = new HashSet<SyntaxNodeId>();
        foreach (var node in module.Descendants())
        {
            switch (node)
            {
                case LiteralExpressionNode { StaticValue: VBStringValue text }:
                    strings.UnionWith(Word.Matches(text.Value).Select(match => match.Value));
                    break;
                // CallByName object, "Name", kind: the member is the one the literal names.
                case IndexExpressionNode { Callee: SimpleNameExpressionNode callee } call
                    when IsCallByName(callee) && NamedByLiteral(call.Arguments) is { } name:
                    namedByLiteral.Add(callee.Identity);
                    members.Add(name);
                    break;
                case CallStatementNode { Callee: SimpleNameExpressionNode callee } call
                    when IsCallByName(callee) && NamedByLiteral(call.Arguments) is { } name:
                    namedByLiteral.Add(callee.Identity);
                    members.Add(name);
                    break;
            }
        }

        var callsByName = false;
        foreach (var (node, _) in NameOccurrences.Walk(module))
        {
            switch (node)
            {
                case MemberAccessExpressionNode access when models.Any(model => model.Expressions.TryGetValue(access.Identity, out var fact)
                    && fact.Flags.HasFlag(ValueExpressionSemanticFlags.LateBound)):
                    members.Add(access.Member.IdentifierName.Trim('[', ']'));
                    break;
                // a procedure of the project named CallByName hides VBA's, and is taken for it: what it does with a string is not known either.
                case SimpleNameExpressionNode name when IsCallByName(name) && !namedByLiteral.Contains(name.Identity):
                    callsByName = true;
                    break;
            }
        }

        return new LateBoundNames(members, callsByName) { NamedInStrings = strings };
    }

    private static bool IsCallByName(SimpleNameExpressionNode name) => string.Equals(name.IdentifierName, CallByName, StringComparison.OrdinalIgnoreCase);

    // the member a CallByName names with a string literal: its second argument, or the one given to ProcName.
    private static string? NamedByLiteral(ImmutableArray<ExpressionNode> arguments)
    {
        if (arguments.IsDefault)
        {
            return null;
        }

        var given = arguments.OfType<NamedArgumentNode>()
            .FirstOrDefault(argument => string.Equals(argument.Name, ProcName, StringComparison.OrdinalIgnoreCase))?.Value
            ?? (arguments.Length > 1 && arguments[1] is not NamedArgumentNode ? arguments[1] : null);
        return given is LiteralExpressionNode { StaticValue: VBStringValue name } ? name.Value : null;
    }
}
