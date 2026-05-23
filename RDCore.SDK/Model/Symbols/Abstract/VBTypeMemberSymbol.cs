using RDCore.SDK.Server.ProtocolExtensions;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.SDK.Model.Symbols.Abstract;

/// <summary>
/// A <c>TypedSymbol</c> representing a symbol that is a <em>member</em> of a parent symbol.
/// </summary>
/// <remarks>
/// All <c>VBTypeMemberSymbol</c> types can be resolved to a <c>VBType</c>; not all <c>VBType</c> are valid MS-VBAL data types.
/// </remarks>
/// <param name="Scope">The allocation scope of the symbol.</param>
/// <param name="WorkspaceRoot">The workspace root for this symbol. For an external project or library, this should be different than the user's project workspace.</param>
/// <param name="Name">The identifier name of the symbol.</param>
/// <param name="Kind">Describes the kind (category) of symbol for the LSP client.</param>
/// <param name="ParentUri">The <c>Uri</c> of the parent symbol.</param>
/// <param name="Range">A <c>Range</c> pointing to the document location that belongs to this symbol.</param>
/// <param name="SelectionRange">A <c>Range</c> pointing to the document location that should be selected when navigating to this symbol.</param>
public abstract record class VBTypeMemberSymbol(Uri WorkspaceRoot, Uri ParentUri, string Name, ScopeKind Scope, SymbolKindExt Kind, Range Range, Range SelectionRange) 
    : TypedSymbol(WorkspaceRoot, ParentUri, Name, Scope, Kind, Range, SelectionRange) { }
