using RDCore.Parsing.Model.Abstract;
using RDCore.Server.ProtocolExtensions;

namespace RDCore.Parsing.Model;

internal record class IgnoredSymbol : Symbol
{
    public IgnoredSymbol(Uri workspaceRoot, string name, string? detail = default, Uri? parentUri = default)
        : base(workspaceRoot, name, SymbolKindExt.Ignored, parentUri)
    {
        Detail = detail;
    }
}