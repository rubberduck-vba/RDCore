using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Server.ProtocolExtensions;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.SDK.Model.Symbols.VBProject;

/// <summary>
/// Represents a specific <c>Function</c> member declaration.
/// </summary>
/// <remarks>
/// A <em>returning member</em> is any type member symbol that statically evaluates to a representable <c>VBType</c>.
/// </remarks>
/// <param name="WorkspaceRoot">The workspace root for this symbol. For an external project or library, this should be different than the user's project workspace.</param>
/// <param name="ParentUri">The <c>Uri</c> of the parent symbol.</param>
/// <param name="Name">The identifier name of the symbol.</param>
/// <param name="Scope">The allocation scope of the symbol.</param>
/// <param name="Kind">Describes the kind (category) of symbol for the LSP client.</param>
/// <param name="Range">A <c>Range</c> pointing to the document location that belongs to this symbol.</param>
/// <param name="SelectionRange">A <c>Range</c> pointing to the document location that should be selected when navigating to this symbol.</param>
/// <param name="ResolvedType">The resolved <c>VBType</c> of this member. Use <c>VBUnknownType</c> if the type isn't resolved yet.</param>
/// <param name="AccessModifier">The access modifier specified for this symbol. Use <c>AccessModifier.Implicit</c> if none is specified.</param>
public record class VBFunctionMemberSymbol(Uri WorkspaceRoot, Uri ParentUri, string Name, ScopeKind Scope, SymbolKindExt Kind, VBType ResolvedType, Range Range, Range SelectionRange, AccessModifier AccessModifier)
    : VBReturningMemberSymbol(WorkspaceRoot, ParentUri, Name, Scope, Kind, ResolvedType, Range, SelectionRange, AccessModifier) { }

/// <summary>
/// Represents a specific unbound <c>Function</c> member declaration.
/// </summary>
/// <remarks>
/// A <em>returning member</em> is any type member symbol that statically evaluates to a representable <c>VBType</c>.
/// </remarks>
/// <param name="Scope">The allocation scope of the symbol.</param>
/// <param name="WorkspaceRoot">The workspace root for this symbol. For an external project or library, this should be different than the user's project workspace.</param>
/// <param name="Name">The identifier name of the symbol.</param>
/// <param name="Kind">Describes the kind (category) of symbol for the LSP client.</param>
/// <param name="ParentUri">The <c>Uri</c> of the parent symbol.</param>
/// <param name="ResolvedType">The resolved <c>VBType</c> of this member. Use <c>VBUnknownType</c> if the type isn't resolved yet.</param>
public record class UnboundVBFunctionMemberSymbol(Uri WorkspaceRoot, Uri ParentUri, string Name, ScopeKind Scope, SymbolKindExt Kind, VBType ResolvedType)
    : UnboundVBReturningMemberSymbol(WorkspaceRoot, ParentUri, Name, Scope, Kind, ResolvedType) { }
