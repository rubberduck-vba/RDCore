using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Server.ProtocolExtensions;

namespace RDCore.SDK.Model.Symbols.VBProject;

/// <summary>
/// A symbol representing a <em>standard module</em>.
/// </summary>
public sealed record class VBStandardModuleSymbol : VBModuleSymbol
{
    /// <summary>
    /// Creates a new <em>standard module</em> symbol.
    /// </summary>
    /// <param name="workspaceRoot">A <c>Uri</c> representing the absolute path to the library or project workspace.</param>
    /// <param name="name">The name of the symbol.</param>
    /// <param name="parentUri">The <c>Uri</c> of the parent symbol.</param>
    public VBStandardModuleSymbol(Uri workspaceRoot, string name, Uri parentUri)
        : base(workspaceRoot, name, SymbolKindExt.Module, parentUri) { }
}
