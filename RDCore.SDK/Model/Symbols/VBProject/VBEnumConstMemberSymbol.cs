using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Server.ProtocolExtensions;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.SDK.Model.Symbols.VBProject;

/// <summary>
/// A member of an <c>VBEnumType</c>.
/// </summary>
/// <remarks>
/// <c>Enum</c> members are considered <c>Public</c> constant member fields declared <c>As Long</c>; their effective visibility is governed by their parent <c>Enum</c>.
/// </remarks>
/// <param name="WorkspaceRoot">The workspace root for this symbol. For an external project or library, this should be different than the user's project workspace.</param>
/// <param name="ParentUri">The <c>Uri</c> of the parent symbol.</param>
/// <param name="Name">The identifier name of the symbol.</param>
/// <param name="Scope">The allocation scope of the symbol.</param>
/// <param name="Kind">Describes the kind (category) of symbol for the LSP client.</param>
/// <param name="Range">A <c>Range</c> pointing to the document location that belongs to this symbol.</param>
/// <param name="SelectionRange">A <c>Range</c> pointing to the document location that should be selected when navigating to this symbol.</param>
public sealed record class VBEnumConstMemberSymbol(Uri WorkspaceRoot, Uri ParentUri, string Name, ScopeKind Scope, SymbolKindExt Kind, Range Range, Range SelectionRange) 
    : VBReturningMemberSymbol(WorkspaceRoot, ParentUri, Name, Scope, Kind, VBLongType.TypeInfo, Range, SelectionRange, AccessModifier.Implicit) { }
