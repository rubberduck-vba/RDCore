using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Server.ProtocolExtensions;

namespace RDCore.SDK.Model.Symbols.Unbound;

/// <summary>
/// An unbound <c>Symbol</c> representing a library (external) class module.
/// </summary>
/// <param name="WorkspaceRoot">A <c>Uri</c> representing the absolute path to the library or project workspace.</param>
/// <param name="ParentUri">The <c>Uri</c> of the parent symbol.</param>
/// <param name="Name">The name of the module.</param>
public record class UnboundClassModuleSymbol(Uri WorkspaceRoot, Uri ParentUri, string Name)
    : Symbol(WorkspaceRoot, ParentUri, Name, ScopeKind.Global, SymbolKindExt.Class) { }
