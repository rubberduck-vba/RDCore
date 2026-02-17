using RDCore.Server.ProtocolExtensions;

namespace RDCore.Parsing.Model.Abstract;

internal abstract record class ModuleSymbol : Symbol
{
    protected ModuleSymbol(Uri workspaceRoot, string name, SymbolKindExt kind, Uri? parentUri = null)
        : base(workspaceRoot, name, kind, parentUri)
    {
    }

    public bool OptionExplicit { get; init; }
    public int? OptionBase { get; init; }
    public bool OptionStrict { get; init; }

    public VBOptionCompare? OptionCompare { get; init; }
}
