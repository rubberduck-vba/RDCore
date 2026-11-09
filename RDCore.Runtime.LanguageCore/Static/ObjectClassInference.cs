using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.Completion;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Libraries;
using RDCore.Runtime.LanguageCore.Static.Abstract;
using System.Collections.Immutable;
using RDCore.SDK.Semantics;
using ExpressionClassification = RDCore.SDK.Semantics.ExpressionClassification;

namespace RDCore.Runtime.LanguageCore.Static;

// MS-VBAL §5.6.12, §6.1.2.8.1.4
public static class ObjectClassInference
{
    public static ImmutableArray<ObjectCreationFact> CandidateClassesOf(
        StaticEvaluationContext context, ExpressionNode receiver, ScopeTree tree, Uri moduleUri, ModuleNode module)
    {
        if (receiver is not SimpleNameExpressionNode name || VariableOf(context, name) is not { } variable)
        {
            return [];
        }

        var candidates = ImmutableArray.CreateBuilder<ObjectCreationFact>();
        foreach (var (assignment, scope) in SetAssignmentsOf(context, name, variable, tree, moduleUri, module))
        {
            var assigned = new ExpressionFactCollector();
            ExpressionStaticSemanticsEvaluator.Evaluate(context with { Scope = scope, EnclosingWithTargetType = null, Facts = assigned, Bindings = null }, assignment.Value);
            if (assigned.TryGet(assignment.Value.Identity, out var value) && value.Creates is { } creates
                && !candidates.Any(candidate => IsSameClass(candidate, creates)))
            {
                candidates.Add(creates);
            }
        }

        return candidates.ToImmutable();
    }

    public static VBClassType? ClassOf(ObjectCreationFact candidate, ISymbolResolver resolver, Uri origin, Func<string, IReadOnlyList<Symbol>>? described)
    {
        var classModule = candidate.Source == ObjectClassSource.UnreferencedLibrary
            ? described?.Invoke(candidate.Library).OfType<VBClassModuleSymbol>().FirstOrDefault(symbol =>
                string.Equals(symbol.Name, candidate.ClassName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(symbol.GetProperty(SymbolProperties.Library), candidate.Library, StringComparison.OrdinalIgnoreCase))
            : VBProjectSymbol.ResolveQualifiedType(resolver, candidate.Library, candidate.ClassName, origin).Symbol as VBClassModuleSymbol;

        return classModule is null ? null : VBClassType.FromClassModule(classModule);
    }

    // the variable a name is, when it is one declared Object or Variant.
    internal static SemanticId? VariableOf(StaticEvaluationContext context, SimpleNameExpressionNode name)
    {
        var facts = new ExpressionFactCollector();
        var result = ExpressionStaticSemanticsEvaluator.Evaluate(context with { Facts = facts, Bindings = null }, name);
        return result.IsSuccess && result.Result is VBObjectType or VBVariantType
            && facts.TryGet(name.Identity, out var fact) && fact is { Classification: ExpressionClassification.Variable, Binding: { } variable }
            ? variable
            : null;
    }

    // every Set statement of the module that assigns the variable, and the scope it is written in.
    internal static IEnumerable<(AssignmentStatementNode Assignment, LexicalScope Scope)> SetAssignmentsOf(
        StaticEvaluationContext context, SimpleNameExpressionNode name, SemanticId variable, ScopeTree tree, Uri moduleUri, ModuleNode module)
    {
        foreach (var assignment in module.Descendants().OfType<AssignmentStatementNode>())
        {
            if (assignment is { Kind: AssignmentKind.Set, Target: SimpleNameExpressionNode target }
                && string.Equals(target.IdentifierName, name.IdentifierName, StringComparison.OrdinalIgnoreCase)
                && Binds(context, tree, moduleUri, target, variable))
            {
                yield return (assignment, ScopeOf(tree, moduleUri, target));
            }
        }
    }

    // whether a name written in the module is the variable: a name is resolved in the scope it is written in, and a local of another procedure of the same name
    // is another variable.
    internal static bool Binds(StaticEvaluationContext context, ScopeTree tree, Uri moduleUri, SimpleNameExpressionNode name, SemanticId variable)
        => context.Resolver.ResolveValue(name, ScopeKind.Local, ScopeOf(tree, moduleUri, name).Uri).Symbol?.SemanticId == variable;

    private static LexicalScope ScopeOf(ScopeTree tree, Uri moduleUri, ExpressionNode name)
        => tree.ScopeFor(CompletionCandidates.OriginAt(tree, moduleUri, name.Location.Range.Start));

    // `Scripting.Dictionary` and `scripting.dictionary` are one class.
    private static bool IsSameClass(ObjectCreationFact x, ObjectCreationFact y)
        => string.Equals(x.Library, y.Library, StringComparison.OrdinalIgnoreCase) && string.Equals(x.ClassName, y.ClassName, StringComparison.OrdinalIgnoreCase);
}
