using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Source;

namespace RDCore.SDK.Model.Symbols.Abstract;

/// <summary>
/// A <c>BoundSymbol</c> (bound to a workspace document <c>Location</c>) that can be resolved to a <c>VBType</c>.
/// </summary>
/// <param name="WorkspaceRoot">A <c>Uri</c> representing the absolute path to the library or project workspace that defines this symbol.</param>
/// <param name="ParentUri">The <c>Uri</c> of the parent symbol.</param>
/// <param name="Name">The name of the symbol.</param>
/// <param name="Scope">The allocation scope of this symbol.</param>
/// <param name="Kind">A <c>SymbolKind</c> (extensible) metadata value describing the kind of symbol.</param>
/// <param name="Range">The entire document <c>Range</c> belonging to this symbol.</param>
/// <param name="SelectionRange">The specific document <c>Range</c> to highlight when this symbol is selected, usually the symbol's <em>identifier</em> name if applicable.</param>
/// <param name="ResolvedType">The resolved <c>VBType</c> of the symbol, if available. <c>VBUnknownType</c> unless specified otherwise.</param>
public abstract record class BoundTypedSymbol(Uri WorkspaceRoot, Uri ParentUri, string Name, ScopeKind Scope, SymbolKindExt Kind, SourceRange Range, SourceRange SelectionRange, VBType ResolvedType)
    : BoundSymbol(WorkspaceRoot, ParentUri, Name, Scope, Kind, Range, SelectionRange), ITypedSymbol { }

/// <summary>
/// An <c>UnboundSymbol</c> (<strong>not</strong> bound to a workspace document <c>Location</c>) that can be resolved to a <c>VBType</c>.
/// </summary>
/// <param name="WorkspaceRoot">A <c>Uri</c> representing the absolute path to the library or project workspace that defines this symbol.</param>
/// <param name="ParentUri">The <c>Uri</c> of the parent symbol.</param>
/// <param name="Name">The name of the symbol.</param>
/// <param name="Scope">The allocation scope of this symbol.</param>
/// <param name="Kind">A <c>SymbolKind</c> (extensible) metadata value describing the kind of symbol.</param>
/// <param name="ResolvedType">The resolved <c>VBType</c> of the symbol, if available. <c>VBUnknownType</c> unless specified otherwise.</param>
public abstract record class UnboundTypedSymbol(Uri WorkspaceRoot, Uri ParentUri, string Name, ScopeKind Scope, SymbolKindExt Kind, VBType ResolvedType)
    : UnboundSymbol(WorkspaceRoot, ParentUri, Name, Scope, Kind), ITypedSymbol { }

/// <summary>
/// A <see cref="Symbol"/> whose declared <see cref="VBType"/> is known, whether or not the symbol
/// itself is bound to a source location.
/// </summary>
/// <remarks>
/// Unifies <see cref="BoundTypedSymbol"/> and <see cref="UnboundTypedSymbol"/> for a caller — a
/// static-semantics rule reading the declared type off a resolved <c>Symbol</c>, chiefly — that does
/// not care which of the two it got.
/// </remarks>
public interface ITypedSymbol
{
    /// <summary>The resolved <c>VBType</c> of the symbol, if available. <c>VBUnknownType</c> unless specified otherwise.</summary>
    VBType ResolvedType { get; }
}
