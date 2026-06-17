using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Server.ProtocolExtensions;

namespace RDCore.SDK.Model.Symbols.Unbound;

/// <summary>
/// An unbound symbol representing any type of module.
/// </summary>
/// <param name="WorkspaceRoot">A <c>Uri</c> representing the absolute path to the library or project workspace.</param>
/// <param name="ParentUri">The <c>Uri</c> of the parent symbol.</param>
/// <param name="Name">The name of the module, as specified by its <c>VB_Attribute.Name</c>.</param>
/// <param name="Scope">The allocation scope of the symbol.</param>
/// <param name="Kind">A <c>SymbolKind</c> (extended, LSP-compliant) metadata value describing the kind of symbol.</param>
public abstract record class VBModuleSymbol(Uri WorkspaceRoot, Uri ParentUri, string Name, ScopeKind Scope, SymbolKindExt Kind)
    : Symbol(WorkspaceRoot, ParentUri, Name, Scope, Kind) { }
