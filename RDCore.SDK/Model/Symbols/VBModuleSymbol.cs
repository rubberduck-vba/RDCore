using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Source;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.Symbols;

/// <summary>
/// An unbound symbol representing any type of module.
/// </summary>
/// <param name="WorkspaceRoot">A <c>Uri</c> representing the absolute path to the library or project workspace.</param>
/// <param name="ParentUri">The <c>Uri</c> of the parent symbol.</param>
/// <param name="Name">The name of the module, as specified by its <c>VB_Attribute.Name</c>.</param>
/// <param name="Scope">The allocation scope of the symbol.</param>
/// <param name="Kind">A <c>SymbolKind</c> (extended, LSP-compliant) metadata value describing the kind of symbol.</param>
public abstract record class VBModuleSymbol(Uri WorkspaceRoot, Uri ParentUri, string Name, ScopeKind Scope, SymbolKindExt Kind)
    : Symbol(WorkspaceRoot, ParentUri, Name, Scope, Kind)
{
    /// <summary>
    /// The module-level directives this module was declared under.
    /// </summary>
    public ModuleDirectives Directives { get; init; } = ModuleDirectives.None;

    /// <summary>
    /// This module's direct members — fields, procedures, properties, and any module-level <c>Type</c>
    /// or <c>Enum</c> declaration — in declaration order. Empty until a workspace-wide pass populates
    /// it (<see cref="RDCore.SDK.Model.Types.Complex.IVBMemberOwnerType"/>'s same "carry it on the
    /// symbol" pattern <c>VBUserDefinedTypeMemberSymbol.Members</c> uses, just a second pass here
    /// instead of falling out of a single AST node's own children: a module's members are separate
    /// top-level declarations, not nested inside the module's own declaration).
    /// </summary>
    public ImmutableArray<VBTypeMemberSymbol> Members { get; init; } = [];
}
