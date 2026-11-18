using System.Collections.Immutable;
using RDCore.Runtime.LanguageCore.Symbols;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;

namespace RDCore.Runtime.LanguageCore.Services;

// MS-VBAL §5.2.4.2
// resolved over the whole set, and again on any change: an interface defined later, or edited since, is otherwise a stale snapshot.
// recursive and memoized by Uri: a flat pass leaves Widget Implements IMiddle Implements IBase looking empty from Widget.
// a name that is no class, the class itself, or a repeat is dropped here: ImplementsSemantics reports it.
// MS-VBAL §4.1, §5.6.10: a name is the class of the implementing class's own project first, then of the libraries by their precedence; a referenced
// source project's class only where the project exposes it (§5.2.4.1.1). The code of a referenced project does not see the session's own classes.
public static class ImplementedInterfaceResolution
{
    public static IReadOnlyDictionary<string, VBClassModuleSymbol> Resolve(IEnumerable<VBClassModuleSymbol> classModules)
    {
        var all = classModules.ToList();
        var byName = all.GroupBy(classModule => classModule.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        VBClassModuleSymbol? Named(string name, VBClassModuleSymbol implementing)
        {
            if (!byName.TryGetValue(name, out var candidates))
            {
                return null;
            }

            var library = implementing.GetProperty(SymbolProperties.Library);
            return candidates.FirstOrDefault(candidate => string.Equals(candidate.GetProperty(SymbolProperties.Library), library, StringComparison.Ordinal))
                ?? candidates
                    .Where(candidate => candidate.GetProperty(SymbolProperties.Library) is not null
                        && (!candidate.GetProperty(SymbolProperties.OfSourceProject) || ReferencedProjectSymbols.IsAccessible(candidate)))
                    .OrderBy(candidate => candidate.GetProperty(SymbolProperties.LibraryPriority))
                    .FirstOrDefault();
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
                if (Named(name, classModule) is { } found
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
