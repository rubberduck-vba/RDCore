using RDCore.Parsing.Model.Symbols.Abstract;
using RDCore.SDK.Server.ProtocolExtensions;

namespace RDCore.Parsing.Model.Symbols;

internal abstract record class ModuleSymbol : Symbol
{
    protected ModuleSymbol(Uri workspaceRoot, string name, SymbolKindExt kind, Uri? parentUri = null)
        : base(workspaceRoot, name, kind, parentUri)
    {
    }

    public override string Name => Get(SymbolProperties.Name) ?? base.Name;
}
