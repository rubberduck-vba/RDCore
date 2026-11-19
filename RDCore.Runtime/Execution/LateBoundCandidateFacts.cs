using System.Collections.Immutable;
using RDCore.Runtime.LanguageCore.Static;
using RDCore.Runtime.LanguageCore.Static.Abstract;
using RDCore.Runtime.LanguageCore.Symbols;
using RDCore.SDK.Model.Symbols.Completion;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics.Facts;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Runtime.Execution;

// MS-VBAL §5.6.12, §5.6.3: what a late-bound member access may reach when it runs is a member of its name of the class of the object, as code of the
// access's project sees the class (§5.3.1.1: a Friend member is its own project's). The sweep evaluates each instruction once and follows no path, so the
// object it accesses the member on says nothing of the classes the code may give it: those are the classes of the objects the module's Set statements give
// the variable (AssignedClasses), when nothing else can assign it. An object of a class that another class implements may be of that other class, and is
// not known.
internal static class LateBoundCandidateFacts
{
    // the facts of a procedure, each late-bound member access with the members it may reach, when the classes of its object are known.
    public static RuntimeFacts Fill(
        RuntimeFacts facts, IRuntimeSession analysis, Uri moduleUri, ModuleNode module, Lazy<IReadOnlyDictionary<SyntaxNodeId, SyntaxNode>> nodes)
    {
        if (!facts.Operations.Any(IsLateBound))
        {
            return facts;
        }

        var tree = analysis.Symbols.Scopes;
        var environment = new StaticEnvironment(analysis.Environment.Is64Bit) { Libraries = analysis.Libraries };
        return facts with
        {
            Operations = [.. facts.Operations.Select(operation => IsLateBound(operation) && operation is MemberAccessFact access
                && nodes.Value.TryGetValue(access.Node, out var node) && node is ExpressionNode expression
                ? access with
                {
                    LateBoundCandidates = CandidatesOf(new StaticEvaluationContext(analysis.Symbols.Resolver,
                        tree.ScopeFor(CompletionCandidates.OriginAt(tree, moduleUri, expression.Location.Range.Start)), Environment: environment),
                        tree, moduleUri, module, analysis.Symbols, access, expression),
                }
                : operation)],
        };
    }

    private static bool IsLateBound(OperatorFact operation)
        => operation is MemberAccessFact { LateBoundCandidates: null } access && access.Flags.HasFlag(MemberAccessOperationSemanticFlags.LateBound);

    private static ImmutableArray<LateBoundCandidate>? CandidatesOf(
        StaticEvaluationContext context, ScopeTree tree, Uri moduleUri, ModuleNode module, ISessionSymbols symbols, MemberAccessFact access,
        ExpressionNode expression)
    {
        if (ObjectOf(module, expression) is not { } receiver
            || ClassesOf(context, tree, moduleUri, module, receiver, symbols) is not { IsEmpty: false } classes)
        {
            return null;
        }

        var project = ProjectVisibility.ProjectOf(context.Scope);
        var candidates = ImmutableArray.CreateBuilder<LateBoundCandidate>();
        foreach (var classModule in classes)
        {
            // a member the code does not name is the default member of the class, which each class says for itself.
            var name = access.Member ?? (access.Flags.HasFlag(MemberAccessOperationSemanticFlags.DefaultMember)
                ? VBClassType.FromClassModule(classModule).DefaultMember?.Name
                : null);
            if (name is null)
            {
                return null;
            }

            var named = classModule.DefaultInterfaceMembers
                .Where(member => member is not VBEventMemberSymbol && string.Equals(member.Name, name, StringComparison.OrdinalIgnoreCase)
                    && ProjectVisibility.Instance.IsVisibleFrom(member, classModule, project))
                .ToArray();

            // an object of an extensible class can have members its class does not declare.
            if (named.Length == 0 && classModule.GetProperty(SymbolProperties.Extensible))
            {
                return null;
            }

            candidates.AddRange(named.Select(member => new LateBoundCandidate(member.Uri, member.AccessModifier)));
        }

        return candidates.ToImmutable();
    }

    // the expression of the object a member is accessed on: before the '.' or the '!', the object given an argument list, the object of the With block the
    // access is in, or the object itself, whose default member is assigned or enumerated.
    private static ExpressionNode? ObjectOf(ModuleNode module, ExpressionNode access) => access switch
    {
        MemberAccessExpressionNode { Owner: { } owner } => owner,
        MemberAccessExpressionNode or DictionaryAccessExpressionNode { Owner: null } => EnclosingWithTargetOf(module, access),
        DictionaryAccessExpressionNode { Owner: { } owner } => owner,
        IndexExpressionNode index => index.Callee,
        _ => access,
    };

    private static ExpressionNode? EnclosingWithTargetOf(ModuleNode module, ExpressionNode access)
        => module.Descendants().OfType<WithStatementNode>()
            .Where(with => with.SourceLocation.Range.Start <= access.Location.Range.Start && access.Location.Range.End <= with.SourceLocation.Range.End)
            .OrderByDescending(with => with.SourceLocation.Range.Start)
            .FirstOrDefault()?.WithExpression;

    // the classes of the objects the module's Set statements give the variable, when they are every object it can hold: none when what can assign it is not
    // all seen. An object of a class that another class implements may be of that other class (§5.3.1.9), and is not known.
    private static ImmutableArray<VBClassModuleSymbol> ClassesOf(
        StaticEvaluationContext context, ScopeTree tree, Uri moduleUri, ModuleNode module, ExpressionNode receiver, ISessionSymbols symbols)
    {
        if (AssignedClasses.Instance.ClassesOf(new PossibleClassesQuery(context, receiver, tree, moduleUri, module)) is not { IsComplete: true } possible)
        {
            return [];
        }

        var classes = ImmutableArray.CreateBuilder<VBClassModuleSymbol>();
        foreach (var assigned in possible.Classes)
        {
            if (!symbols.TryGetModule(assigned.Uri, out var found) || found is not VBClassModuleSymbol classModule || IsImplemented(tree, classModule))
            {
                return [];
            }

            classes.Add(VBProjectSymbol.ResolveClass(context.Resolver, classModule) ?? classModule);
        }

        return classes.ToImmutable();
    }

    private static bool IsImplemented(ScopeTree tree, VBClassModuleSymbol classModule)
        => tree.Global.Declarations.OfType<VBClassModuleSymbol>().Any(other => other.ImplementedInterfaceNames
            .Any(name => string.Equals(name.Split('.')[^1], classModule.Name, StringComparison.OrdinalIgnoreCase)));
}
