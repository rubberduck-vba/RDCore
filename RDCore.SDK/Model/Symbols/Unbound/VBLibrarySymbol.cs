using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Server.ProtocolExtensions;

namespace RDCore.SDK.Model.Symbols.Unbound;

/// <summary>
/// An unbound <c>Symbol</c> that represents a <em>library</em> or <em>project</em>.
/// </summary>
/// <param name="WorkspaceRoot">A <c>Uri</c> representing the absolute path to the library or project workspace.</param>
/// <param name="Name">The name of the symbol.</param>
public abstract record class VBLibrarySymbol(Uri WorkspaceRoot, string Name) 
    : Symbol(WorkspaceRoot, null!, Name, ScopeKind.Global, SymbolKindExt.Project) { }
