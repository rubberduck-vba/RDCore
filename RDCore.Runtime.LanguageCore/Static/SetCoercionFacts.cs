using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Semantics;
using RDCore.Runtime.LanguageCore.Static.Abstract;
using System.Runtime.CompilerServices;

namespace RDCore.Runtime.LanguageCore.Static;

// MS-VBAL §5.5.2.2.1: an object is Set-coerced to a class it is of, or one its class implements (§5.2.4.2), and is error 13 otherwise. A variable declared
// as a class holds an object of that class or of a class that implements it: which of those the destination takes is how the two classes relate, told
// from the Implements statements of the workspace's class modules. A library's class is what its server answers to when the code runs (QueryInterface),
// and is not related here.
internal static class SetCoercionFacts
{
    private static readonly ConditionalWeakTable<LexicalScope, IReadOnlyDictionary<string, IReadOnlySet<string>>> Closures = new();

    // MS-VBAL §5.4.3.9, §5.3.1.11: the value of a Set statement, and an argument Set-assigned to a parameter declared as a class.
    public static void Record(StaticEvaluationContext context, ExpressionNode expression, VBType source, VBType destination)
    {
        if (context.Facts is { } facts && facts.TryGet(expression.Identity, out var fact) && Of(context, source, destination) is { } coercion
            && fact.SetCoercion != coercion)
        {
            facts.Record(fact with { SetCoercion = coercion });
        }
    }

    public static SetCoercionFact? Of(StaticEvaluationContext context, VBType source, VBType destination)
    {
        if (source is not VBClassType { Symbol: var sourceClass } || destination is not VBClassType { Symbol: var destinationClass }
            || SymbolProperties.IsOfALibraryProject(sourceClass) || SymbolProperties.IsOfALibraryProject(destinationClass)
            || ClosuresOf(context) is not { } closures
            || !closures.TryGetValue(sourceClass.Uri.AbsoluteUri, out var sourceIs)
            || !closures.TryGetValue(destinationClass.Uri.AbsoluteUri, out var destinationIs))
        {
            return null;
        }

        var sourceKey = sourceClass.Uri.AbsoluteUri;
        var destinationKey = destinationClass.Uri.AbsoluteUri;
        var relation = string.Equals(sourceKey, destinationKey, StringComparison.Ordinal) ? ClassRelation.Same
            : sourceIs.Contains(destinationKey) ? ClassRelation.Implements
            : destinationIs.Contains(sourceKey) ? ClassRelation.ImplementedBy
            : closures.Values.Any(each => each.Contains(sourceKey) && each.Contains(destinationKey)) ? ClassRelation.SharedImplementer
            : ClassRelation.Unrelated;
        return new SetCoercionFact(destinationClass.Name, relation);
    }

    // each class module of the workspace's projects, by its address, with the addresses of the classes it is: itself, and those it implements, transitively.
    private static IReadOnlyDictionary<string, IReadOnlySet<string>>? ClosuresOf(StaticEvaluationContext context)
        => context.Scope.SelfAndAncestors().FirstOrDefault(scope => scope.Kind == LexicalScopeKind.Global) is { } global
            ? Closures.GetValue(global, static scope => Build(scope.Declarations.OfType<VBClassModuleSymbol>()))
            : null;

    private static IReadOnlyDictionary<string, IReadOnlySet<string>> Build(IEnumerable<VBClassModuleSymbol> classModules)
    {
        var closures = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
        foreach (var classModule in classModules.Where(each => !SymbolProperties.IsOfALibraryProject(each)))
        {
            var key = classModule.Uri.AbsoluteUri;
            if (closures.ContainsKey(key))
            {
                continue;
            }

            var closure = new HashSet<string>(StringComparer.Ordinal);
            Add(classModule, closure);
            closures[key] = closure;
        }

        return closures;
    }

    // ImplementedInterfaces is resolved recursively (ImplementedInterfaceResolution); a cycle stops at a class already seen.
    private static void Add(VBClassModuleSymbol classModule, HashSet<string> closure)
    {
        if (closure.Add(classModule.Uri.AbsoluteUri))
        {
            foreach (var implemented in classModule.ImplementedInterfaces)
            {
                Add(implemented, closure);
            }
        }
    }
}
