using RDCore.Parsing.Model.Abstract;
using RDCore.Server.ProtocolExtensions;

namespace RDCore.Parsing.Model;

internal sealed record class StdModuleSymbol : ModuleSymbol
{
    public StdModuleSymbol(Uri workspaceRoot, string name, Uri? parentUri = null)
        : base(workspaceRoot, name, SymbolKindExt.Module, parentUri)
    {
    }

    public bool OptionPrivateModule { get; init; }
}
