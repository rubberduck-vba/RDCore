using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Server.ProtocolExtensions;

namespace RDCore.SDK.Model.Symbols.Unbound;

/// <summary>
/// Represents an unbound <c>Symbol</c> that can be resolved to a <c>VBType</c>.
/// </summary>
public record class UnboundTypedSymbol : Symbol
{
    /// <summary>
    /// An unbound <c>Symbol</c> that can be resolved to a <c>VBType</c>.
    /// </summary>
    /// <param name="workspaceRoot">A <c>Uri</c> representing the absolute path to the library or project workspace that defines this symbol.</param>
    /// <param name="parentUri">The <c>Uri</c> of the parent symbol.</param>
    /// <param name="name">The name of the symbol.</param>
    /// <param name="resolvedType">The resolved <c>VBType</c> of the symbol, if available. <c>VBUnknownType</c> unless specified otherwise.</param>
    protected UnboundTypedSymbol(
        Uri workspaceRoot,
        Uri parentUri,
        string name,
        ScopeKind scope,
        SymbolKindExt kind,
        VBType? resolvedType = default)
        : base(workspaceRoot, parentUri, name, scope, kind)
    {
        ResolvedType = resolvedType ?? VBUnknownType.TypeInfo;
    }

    /// <summary>
    /// The resolved <c>VBType</c> of the symbol, if available. <c>VBUnknownType</c> unless specified otherwise.
    /// </summary>
    public VBType ResolvedType { get; init; }
}
