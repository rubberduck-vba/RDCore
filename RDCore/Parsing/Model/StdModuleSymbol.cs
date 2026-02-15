using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Parsing.Model.Abstract;

namespace RDCore.Parsing.Model;

internal sealed record class StdModuleSymbol : ModuleSymbol
{
    public StdModuleSymbol(Uri workspaceRoot, string name, Uri? parentUri = null)
        : base(workspaceRoot, name, SymbolKind.Module, parentUri)
    {
    }

    public bool OptionPrivateModule { get; init; }
}
