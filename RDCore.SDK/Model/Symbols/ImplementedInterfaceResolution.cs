using System.Collections.Immutable;

namespace RDCore.SDK.Model.Symbols;

/// <summary>
/// Resolves the interface names of the <c>Implements</c> directives of class modules
/// (<see cref="VBClassModuleSymbol.ImplementedInterfaceNames"/>, <strong>MS-VBAL §5.2.4.2</strong>) to the classes they name.
/// </summary>
/// <remarks>
/// A class module's <see cref="VBClassModuleSymbol.ImplementedInterfaces"/> hold the interface class symbols as they are
/// when it is resolved, so whatever composes class modules - the language server over a workspace, the environment host over
/// the modules it is sent one at a time - resolves them over the whole set, and again when any of them changes: an interface
/// defined after the class that implements it, or edited since, is otherwise a stale snapshot of itself.
/// <para>
/// Resolution is recursive and memoized by <c>Uri</c>, not a flat single pass: a flat pass would assign each class's
/// interfaces from the <em>pre-resolution</em> snapshot, so a transitive chain (<c>Widget Implements IMiddle</c>,
/// <c>IMiddle Implements IBase</c>) would leave <c>IMiddle</c>'s own interfaces looking empty from <c>Widget</c>'s side,
/// whichever order the two classes come in. Recursing into (and caching) each interface's own fully resolved symbol is
/// what makes <see cref="Types.Complex.VBClassType.FromClassModule(VBClassModuleSymbol)"/>'s own recursion see the whole
/// chain from any entry point.
/// </para>
/// <para>
/// A name that is no class among the ones given, one that is the class itself, and one a directive repeats are dropped
/// rather than reported: they are what <see cref="Semantics.Static.ImplementsSemantics"/> reports, from the names.
/// </para>
/// </remarks>
public static class ImplementedInterfaceResolution
{
    /// <summary>
    /// Resolves the implemented interfaces of every class module in <paramref name="classModules"/> that names any.
    /// </summary>
    /// <param name="classModules">Every class module the names can refer to.</param>
    /// <returns>
    /// The class modules that name an interface, resolved, keyed by their <see cref="Symbol.Uri"/> as an absolute URI string - a
    /// <c>Uri</c>'s own equality ignores the fragment, which is where a symbol's identity lives.
    /// </returns>
    public static IReadOnlyDictionary<string, VBClassModuleSymbol> Resolve(IEnumerable<VBClassModuleSymbol> classModules)
    {
        var all = classModules.ToList();
        var byName = new Dictionary<string, VBClassModuleSymbol>(StringComparer.OrdinalIgnoreCase);
        foreach (var classModule in all)
        {
            byName.TryAdd(classModule.Name, classModule);
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
                if (byName.TryGetValue(name, out var found)
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
