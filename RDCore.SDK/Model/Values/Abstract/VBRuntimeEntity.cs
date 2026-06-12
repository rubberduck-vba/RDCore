using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types.Abstract;

namespace RDCore.SDK.Model.Values.Abstract;

/// <summary>
/// Represents a typed entity that can be associated with a <c>Symbol</c>.
/// </summary>
/// <param name="TypeInfo">The <c>VBType</c> of the entity.</param>
/// <param name="ResolvedSymbol">The resolved <c>Symbol</c> represented by the <c>SymbolUri</c>.</param>
public abstract record class VBRuntimeEntity(VBType TypeInfo, Symbol ResolvedSymbol) 
{
    /// <summary>
    /// Gets a unique <em>semantic ID</em> for this entity.
    /// </summary>
    public Uri SemanticId => ResolvedSymbol.Uri;
}
