using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Server.ProtocolExtensions;

namespace RDCore.SDK.Model.Symbols.VBProject;

/// <summary>
/// Represents any <em>class module</em>.
/// </summary>
public record class VBClassModuleSymbol : VBModuleSymbol
{
    /// <summary>
    /// Creates a new <em>class module</em> of the specified kind.
    /// </summary>
    /// <param name="workspaceRoot">A <c>Uri</c> representing the absolute path to the library or project workspace.</param>
    /// <param name="name">The name of the symbol.</param>
    /// <param name="kind">A <c>SymbolKind</c> (extended, LSP-compliant) metadata value describing the kind of symbol.</param>
    /// <param name="parentUri">The <c>Uri</c> of the parent symbol.</param>
    public VBClassModuleSymbol(Uri workspaceRoot, string name, SymbolKindExt kind, Uri parentUri) 
        : base(workspaceRoot, name, kind, parentUri)
    {
    }
}
