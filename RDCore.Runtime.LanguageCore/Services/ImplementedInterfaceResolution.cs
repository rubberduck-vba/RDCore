using System.Collections.Immutable;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;

namespace RDCore.Runtime.LanguageCore.Services;

// MS-VBAL §5.2.4.2
// resolved over the whole set, and again on any change: an interface defined later, or edited since, is otherwise a stale snapshot.
// recursive and memoized by Uri: a flat pass leaves Widget Implements IMiddle Implements IBase looking empty from Widget.
// a name that is no class, the class itself, or a repeat is dropped here: ImplementsSemantics reports it.
public static class ImplementedInterfaceResolution
{
    public static IReadOnlyDictionary<string, VBClassModuleSymbol> Resolve(IEnumerable<VBClassModuleSymbol> classModules)
    {
        var all = classModules.ToList();
        var byName = all.GroupBy(classModule => classModule.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(namesakes => namesakes.Key, namesakes => namesakes.ToArray(), StringComparer.OrdinalIgnoreCase);

        // the name is a type expression of the implementing class (MS-VBAL §5.6.16.7), bound in the type binding context (§5.6.10): a class of its own
        // project comes first, then the standard library's, then each reference's by its precedence (ReferencePrecedence). A library's hidden interface
        // (Excel's IShape) never takes the place of the workspace's class of the same name.
        VBClassModuleSymbol? Named(VBClassModuleSymbol classModule, string name)
        {
            if (!byName.TryGetValue(name, out var namesakes))
            {
                return null;
            }

            var project = classModule.GetProperty(SymbolProperties.Library);
            return namesakes.MinBy(namesake => string.Equals(namesake.GetProperty(SymbolProperties.Library), project, StringComparison.Ordinal)
                ? long.MinValue
                : ReferencePrecedence.RankOf(namesake));
        }

        var resolved = new Dictionary<string, VBClassModuleSymbol>(StringComparer.Ordinal);
        var resolving = new HashSet<string>(StringComparer.Ordinal);

        VBClassModuleSymbol ResolveOne(VBClassModuleSymbol classModule)
        {
            var key = classModule.Uri.AbsoluteUri;
            if (resolved.TryGetValue(key, out var already))
            {
                return already;
            }

            if (!resolving.Add(key))
            {
                // a cycle (MS-VBAL 5.2.3.6 disallows this, not yet validated): stop recursing here rather than looping
                // forever over a malformed workspace.
                return classModule;
            }

            var implemented = ImmutableArray.CreateBuilder<VBClassModuleSymbol>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var name in classModule.ImplementedInterfaceNames)
            {
                if (Named(classModule, name) is { } found
                    && !string.Equals(found.Uri.AbsoluteUri, key, StringComparison.Ordinal)
                    && seen.Add(found.Uri.AbsoluteUri))
                {
                    implemented.Add(ResolveOne(found));
                }
            }

            var result = classModule with { ImplementedInterfaces = implemented.ToImmutable() };
            resolving.Remove(key);
            resolved[key] = result;
            return result;
        }

        var named = new Dictionary<string, VBClassModuleSymbol>(StringComparer.Ordinal);
        foreach (var classModule in all)
        {
            if (!classModule.ImplementedInterfaceNames.IsEmpty)
            {
                named[classModule.Uri.AbsoluteUri] = ResolveOne(classModule);
            }
        }

        return named;
    }
}
