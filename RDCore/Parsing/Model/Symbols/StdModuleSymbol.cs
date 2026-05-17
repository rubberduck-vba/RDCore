using RDCore.SDK.Server.ProtocolExtensions;

namespace RDCore.Parsing.Model.Symbols;

internal sealed record class StdModuleSymbol : ModuleSymbol
{
    public StdModuleSymbol(Uri workspaceRoot, string name, Uri? parentUri = null)
        : base(workspaceRoot, name, SymbolKindExt.Module, parentUri)
    {
    }
}
