using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Server.ProtocolExtensions;

namespace RDCore.SDK.Model.Symbols;

public record class IgnoredSymbol : Symbol
{
    public IgnoredSymbol(Uri workspaceRoot, string name, Uri? parentUri = default)
        : base(workspaceRoot, name, SymbolKindExt.Ignored, parentUri)
    {
    }
}