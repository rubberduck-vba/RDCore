using RDCore.SDK.Model.Symbols;
using System.Diagnostics.CodeAnalysis;

namespace RDCore.SDK.Runtime.Abstract.Execution;

/// <summary>
/// Finds a module of a session by its name, whichever kind of module it is.
/// </summary>
public static class SessionSymbolsModuleExtensions
{
    /// <summary>
    /// Resolves the module named <paramref name="moduleName"/>: a standard module or a class module.
    /// </summary>
    /// <remarks>
    /// A standard module is a value in the default binding context, and a class module is a type: the name of one binds only in the type binding
    /// context (<strong>MS-VBAL §5.6.4</strong>), unless the class has a default instance. The name is looked for in the one, then in the other, so that
    /// what is read off the module symbol - its <see cref="VBModuleSymbol.Directives"/> - is a class module's as much as a standard module's.
    /// </remarks>
    /// <param name="symbols">The symbols of the session.</param>
    /// <param name="moduleName">The name of the module, as the project knows it.</param>
    /// <param name="module">The module symbol, when the name is one.</param>
    /// <returns><see langword="false"/> if the session has no module of that name.</returns>
    public static bool TryResolveModule(this ISessionSymbols symbols, string moduleName, [NotNullWhen(true)] out VBModuleSymbol? module)
    {
        if ((symbols.TryResolveValue(moduleName, GlobalSymbols.UnresolvedSymbol, out var resolved)
                || symbols.TryResolveType(moduleName, GlobalSymbols.UnresolvedSymbol, out resolved))
            && resolved is VBModuleSymbol found)
        {
            module = found;
            return true;
        }

        module = null;
        return false;
    }
}
