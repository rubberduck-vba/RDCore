using RDCore.SDK.Server.ProtocolExtensions;

namespace RDCore.SDK.Model.Symbols.Abstract;

/// <summary>
/// Represents any type of module symbol.
/// </summary>
public abstract record class VBModuleSymbol : Symbol
{
    /// <summary>
    /// Creates a new module symbol of the specified kind.
    /// </summary>
    /// <param name="workspaceRoot">A <c>Uri</c> representing the absolute path to the library or project workspace.</param>
    /// <param name="name">The name of the symbol.</param>
    /// <param name="kind">A <c>SymbolKind</c> (extended, LSP-compliant) metadata value describing the kind of symbol.</param>
    /// <param name="parentUri">The <c>Uri</c> of the parent symbol.</param>
    protected VBModuleSymbol(Uri workspaceRoot, string name, SymbolKindExt kind, Uri parentUri)
        : base(workspaceRoot, name, kind, parentUri)
    {
    }

    /// <summary>
    /// The name of the symbol.
    /// </summary>
    /// <remarks>
    /// This value is supplied by a <c>VB_Attribute.Name</c> value.
    /// </remarks>
    public override string Name => GetProperty(SymbolProperties.Name) ?? base.Name;
}
