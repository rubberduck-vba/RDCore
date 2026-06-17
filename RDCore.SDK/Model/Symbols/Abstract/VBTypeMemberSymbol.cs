using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Server.ProtocolExtensions;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.SDK.Model.Symbols.Abstract;

/// <summary>
/// A <c>AccessibleTypedSymbol</c> representing a symbol that is a <em>member</em> (child) of a parent symbol.
/// </summary>
/// <remarks>
/// All <c>VBTypeMemberSymbol</c> types can be resolved to a <c>VBType</c>; not all <c>VBType</c> are valid MS-VBAL data types.
/// The parent symbol should implement the <c>IVBMemberOwnerType</c> interface. 
/// </remarks>
/// <param name="WorkspaceRoot">A <c>Uri</c> representing the absolute path to the library or project workspace that defines this symbol.</param>
/// <param name="ParentUri">The <c>Uri</c> of the parent symbol.</param>
/// <param name="Name">The name of the symbol.</param>
/// <param name="Scope">The allocation scope of this symbol.</param>
/// <param name="Kind">A <c>SymbolKind</c> (extensible) metadata value describing the kind of symbol.</param>
/// <param name="ResolvedType">The resolved <c>VBType</c> of the symbol, if available. <c>VBUnknownType</c> unless specified otherwise.</param>
/// <param name="Range">The entire document <c>Range</c> belonging to this symbol.</param>
/// <param name="SelectionRange">The specific document <c>Range</c> to highlight when this symbol is selected, usually the symbol's <em>identifier</em> name if applicable.</param>
/// <param name="AccessModifier">The access modifier specified for this symbol. <c>AccessModifier.Implicit</c> unless specified otherwise.</param>
public abstract record class VBTypeMemberSymbol(Uri WorkspaceRoot, Uri ParentUri, string Name, ScopeKind Scope, SymbolKindExt Kind, VBType ResolvedType, Range Range, Range SelectionRange, AccessModifier AccessModifier)
    : AccessibleTypedSymbol(WorkspaceRoot, ParentUri, Name, Scope, Kind, ResolvedType, Range, SelectionRange, AccessModifier) { }

/// <summary>
/// An unbound <c>UnboundTypedSymbol</c> representing a symbol that is a <em>member</em> (child) of a parent symbol.
/// </summary>
/// <remarks>
/// All <c>VBTypeMemberSymbol</c> types can be resolved to a <c>VBType</c>; not all <c>VBType</c> are valid MS-VBAL data types.
/// The parent symbol should implement the <c>IVBMemberOwnerType</c> interface. 
/// </remarks>
/// <param name="WorkspaceRoot">A <c>Uri</c> representing the absolute path to the library or project workspace that defines this symbol.</param>
/// <param name="ParentUri">The <c>Uri</c> of the parent symbol.</param>
/// <param name="Name">The name of the symbol.</param>
/// <param name="Scope">The allocation scope of this symbol.</param>
/// <param name="Kind">A <c>SymbolKind</c> (extensible) metadata value describing the kind of symbol.</param>
/// <param name="ResolvedType">The resolved <c>VBType</c> of the symbol, if available. <c>VBUnknownType</c> unless specified otherwise.</param>
public abstract record class UnboundVBTypeMemberSymbol(Uri WorkspaceRoot, Uri ParentUri, string Name, ScopeKind Scope, SymbolKindExt Kind, VBType ResolvedType)
    : UnboundTypedSymbol(WorkspaceRoot, ParentUri, Name, Scope, Kind, ResolvedType) { }
