using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Server.ProtocolExtensions;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.SDK.Model.Symbols.VBProject;

/// <summary>
/// Represents an <c>Enum</c> type declaration symbol.
/// </summary>
/// <param name="Scope">The allocation scope of the symbol.</param>
/// <param name="WorkspaceRoot">The workspace root for this symbol. For an external project or library, this should be different than the user's project workspace.</param>
/// <param name="Name">The identifier name of the symbol.</param>
/// <param name="Kind">Describes the kind (category) of symbol for the LSP client.</param>
/// 
/// <param name="ParentUri">The <c>Uri</c> of the parent symbol.</param>
/// <param name="Range">A <c>Range</c> pointing to the document location that belongs to this symbol.</param>
/// <param name="SelectionRange">A <c>Range</c> pointing to the document location that should be selected when navigating to this symbol.</param>
public sealed record class VBEnumMemberSymbol(Uri WorkspaceRoot, Uri ParentUri, string Name, ScopeKind Scope, SymbolKindExt Kind, VBType ResolvedType, Range Range, Range SelectionRange, AccessModifier AccessModifier) 
    : VBTypeMemberSymbol(WorkspaceRoot, ParentUri, Name, Scope, Kind, ResolvedType, Range, SelectionRange, AccessModifier) { }

/// <summary>
/// Represents an <c>Enum</c> type declaration symbol.
/// </summary>
/// <param name="Scope">The allocation scope of the symbol.</param>
/// <param name="WorkspaceRoot">The workspace root for this symbol. For an external project or library, this should be different than the user's project workspace.</param>
/// <param name="Name">The identifier name of the symbol.</param>
/// <param name="Kind">Describes the kind (category) of symbol for the LSP client.</param>
/// <param name="ParentUri">The <c>Uri</c> of the parent symbol.</param>
public sealed record class UnboundVBEnumMemberSymbol(Uri WorkspaceRoot, Uri ParentUri, string Name, ScopeKind Scope, SymbolKindExt Kind, VBType ResolvedType)
    : UnboundVBTypeMemberSymbol(WorkspaceRoot, ParentUri, Name, Scope, Kind, ResolvedType)
{ }
