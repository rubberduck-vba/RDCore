using RDCore.Parsing.Model.Abstract;
using RDCore.Server.ProtocolExtensions;

namespace RDCore.Parsing.Model;

internal record class ClassModuleSymbol : ModuleSymbol
{
    public ClassModuleSymbol(Uri workspaceRoot, string name, Instancing instancing = Instancing.Private, bool predeclaredId = false, Uri? parentUri = null)
        : base(workspaceRoot, name, SymbolKindExt.Class, parentUri)
    {
        Instancing = instancing;
        PredeclaredId = predeclaredId;
    }

    public Instancing Instancing { get; init; }
    public bool PredeclaredId { get; init; }

    public ClassModuleSymbol WithInstancing(Instancing instancing) => this with { Instancing = instancing };
    public ClassModuleSymbol WithPredeclaredId(bool value = true) => this with { PredeclaredId = value };
}
