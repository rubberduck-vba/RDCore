using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Server.ProtocolExtensions;

namespace RDCore.SDK.Model.Symbols.Unbound;

/// <summary>
/// An unbound symbol representing a <em>standard module</em>.
/// </summary>
/// <param name="WorkspaceRoot">A <c>Uri</c> representing the absolute path to the library or project workspace.</param>
/// <param name="ParentUri">The <c>Uri</c> of the parent symbol.</param>
/// <param name="Name">The name of the module, as specified by its <c>VB_Attribute.Name</c>.</param>
public record class VBStandardModuleSymbol(Uri WorkspaceRoot, Uri ParentUri, string Name) 
    : VBModuleSymbol(WorkspaceRoot, ParentUri, Name, ScopeKind.Module, SymbolKindExt.Module) { }
