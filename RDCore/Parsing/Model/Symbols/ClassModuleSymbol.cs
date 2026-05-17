using RDCore.SDK.Server.ProtocolExtensions;

namespace RDCore.Parsing.Model.Symbols;

internal record class ClassModuleSymbol : ModuleSymbol
{
    public ClassModuleSymbol(Uri workspaceRoot, string name, Instancing instancing = Instancing.Private, Uri? parentUri = null)
        : base(workspaceRoot, name, SymbolKindExt.Class, parentUri)
    {
    }
}
