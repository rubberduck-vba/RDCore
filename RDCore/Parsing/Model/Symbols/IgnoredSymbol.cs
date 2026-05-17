using RDCore.Parsing.Model.Symbols.Abstract;
using RDCore.SDK.Server.ProtocolExtensions;

namespace RDCore.Parsing.Model.Symbols;

internal record class IgnoredSymbol : Symbol
{
    public IgnoredSymbol(Uri workspaceRoot, string name, Uri? parentUri = default)
        : base(workspaceRoot, name, SymbolKindExt.Ignored, parentUri)
    {
    }
}