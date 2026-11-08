using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;

namespace RDCore.SDK.Semantics.Flow;

/// <summary>
/// The symbol a binding of the static pass names, for the analyses that need to know what kind of variable it is.
/// </summary>
internal static class FlowSymbols
{
    /// <summary>
    /// The symbol whose identity is <paramref name="id"/>, when one of the scopes declares it.
    /// </summary>
    /// <param name="scopes">The scopes of the workspace.</param>
    /// <param name="id">The binding.</param>
    public static Symbol? SymbolOf(ScopeTree scopes, SemanticId id)
        => scopes.TryGetScope(id.Uri, out var scope)
            ? scope.SelfAndAncestors().SelectMany(outer => outer.Declarations).FirstOrDefault(symbol => symbol.SemanticId == id)
            : null;
}
