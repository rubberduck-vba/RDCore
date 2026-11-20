using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Runtime.LanguageCore.Static;

// MS-VBAL §5.3.1.8, §5.3.1.9, §5.3.1.10: whether the implementations of an interface member, or the handlers of an event, refer to the parameter at the
// position of each of its own (DeclarationFact.IsReferredToByABoundMember), over the modules of a project. The members bound to it are those the host binds
// (DeclarationFact.Binding), in any module of the project. Not stated for a member nothing is bound to, nor for one a referencing project may bind to: a
// member of an exposed class, or of a library project. Where a member bound to it does not say what refers to its parameter, the answer is not known either.
internal static class BoundParameters
{
    public static IReadOnlyCollection<ModuleSemanticModel> Stated(
        IReadOnlyCollection<ModuleSemanticModel> models, IReadOnlyList<VBModuleSymbol> project, Func<VBModuleSymbol, IEnumerable<VBTypeMemberSymbol>> membersOf,
        bool isLibrary)
    {
        if (isLibrary)
        {
            return models;
        }

        var members = project.SelectMany(membersOf).GroupBy(member => member.SemanticId).ToDictionary(group => group.Key, group => group.First());
        var facts = models.SelectMany(model => model.Declarations).GroupBy(fact => fact.Symbol).ToDictionary(group => group.Key, group => group.First());
        // a Uri's fragment is where a symbol's identity lives: the members of an exposed module are found by the module, not by their own address.
        var exposedModules = models.Where(model => model.Class is { } flags && flags.HasFlag(ClassModuleSemanticFlags.Exposed))
            .Select(model => model.Module.AbsoluteUri)
            .ToHashSet(StringComparer.Ordinal);
        var exposed = project.Where(module => exposedModules.Contains(module.Uri.AbsoluteUri))
            .SelectMany(membersOf)
            .Select(member => member.SemanticId)
            .ToHashSet();

        // each parameter of a bound member, and what refers to the parameter at its position in each member bound to it; null where that is not known.
        var referred = new Dictionary<SemanticId, bool?>();
        foreach (var fact in models.SelectMany(model => model.Declarations))
        {
            if (fact.Binding is not { Bound: { } bound } || fact.Role is not (DeclarationRole.InterfaceImplementation or DeclarationRole.EventHandler)
                || !members.TryGetValue(bound, out var boundMember) || exposed.Contains(bound)
                || !members.TryGetValue(fact.Symbol, out var member))
            {
                continue;
            }

            var declared = DeclarationStaticSemanticsEvaluator.ParametersOf(boundMember);
            var own = DeclarationStaticSemanticsEvaluator.ParametersOf(member);
            // the handler of an event of a control array is told which control raised it before the event's own parameters.
            var offset = Math.Max(0, own.Length - declared.Length);
            for (var index = 0; index < declared.Length && offset + index < own.Length; index++)
            {
                bool? refersToIt = facts.TryGetValue(own[offset + index].SemanticId, out var parameter) && parameter.References is { } references
                    ? references.Total > 0
                    : null;
                var key = declared[index].SemanticId;
                referred[key] = referred.TryGetValue(key, out var others) ? Either(others, refersToIt) : refersToIt;
            }
        }

        if (referred.Count == 0)
        {
            return models;
        }

        return [.. models.Select(model => model.Declarations.Any(fact => referred.ContainsKey(fact.Symbol))
            ? model with
            {
                Declarations = [.. model.Declarations.Select(fact => referred.TryGetValue(fact.Symbol, out var isReferred)
                    ? fact with { IsReferredToByABoundMember = isReferred }
                    : fact)],
            }
            : model)];
    }

    // one member that refers to it is enough; otherwise, one that does not say keeps the answer unknown.
    private static bool? Either(bool? one, bool? other) => one == true || other == true ? true : one is null || other is null ? null : false;
}
