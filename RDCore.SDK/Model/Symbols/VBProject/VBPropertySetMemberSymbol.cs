using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Types.Abstract;

namespace RDCore.SDK.Model.Symbols.VBProject;

/// <summary>
/// Represents a <c>Property Set</c> procedure member declaration symbol.
/// </summary>
/// <param name="WorkspaceRoot">The workspace root for this symbol. For an external project or library, this should be different than the user's project workspace.</param>
/// <param name="ParentUri">The <c>Uri</c> of the parent symbol.</param>
/// <param name="Name">The identifier name of the symbol.</param>
/// <param name="Scope">The allocation scope of the symbol.</param>
/// <param name="Kind">Describes the kind (category) of symbol for the LSP client.</param>
/// <param name="Range">A <c>Range</c> pointing to the document location that belongs to this symbol.</param>
/// <param name="SelectionRange">A <c>Range</c> pointing to the document location that should be selected when navigating to this symbol.</param>
/// <param name="AccessModifier">The access modifier specified for this symbol. Use <c>AccessModifier.Implicit</c> if none is specified.</param>
public sealed record class VBPropertySetMemberSymbol(Uri WorkspaceRoot, Uri ParentUri, string Name, ScopeKind Scope, SymbolKindExt Kind, VBType ResolvedType, SourceRange Range, SourceRange SelectionRange, AccessModifier AccessModifier)
    : VBProcedureMemberSymbol(WorkspaceRoot, ParentUri, Name, Scope, Kind, ResolvedType, Range, SelectionRange, AccessModifier), IVBPropertyMemberSymbol
{
    /// <remarks>
    /// The property's <c>Get</c>, <c>Let</c> and <c>Set</c> accessors share a name, and each defines a scope of its
    /// own — its parameters and locals. The <c>Get</c> accessor keeps the property's own identity; a <c>Set</c>
    /// accessor is addressed by the reserved word <c>Set</c> after the name, which no local or parameter can be.
    /// </remarks>
    protected override string? UriSuffix => "Set";
}

/// <summary>
/// Represents an unbound <c>Property Set</c> procedure member declaration symbol.
/// </summary>
/// <param name="WorkspaceRoot">The workspace root for this symbol. For an external project or library, this should be different than the user's project workspace.</param>
/// <param name="ParentUri">The <c>Uri</c> of the parent symbol.</param>
/// <param name="Name">The identifier name of the symbol.</param>
/// <param name="Scope">The allocation scope of the symbol.</param>
/// <param name="Kind">Describes the kind (category) of symbol for the LSP client.</param>
public sealed record class UnboundVBPropertySetMemberSymbol(Uri WorkspaceRoot, Uri ParentUri, string Name, ScopeKind Scope, SymbolKindExt Kind, VBType ResolvedType)
    : UnboundVBProcedureMemberSymbol(WorkspaceRoot, ParentUri, Name, Scope, Kind, ResolvedType), IVBPropertyMemberSymbol
{ }
