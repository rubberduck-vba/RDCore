using RDCore.Parsing.Model.Symbols.Abstract;
using RDCore.Server.ProtocolExtensions;

namespace RDCore.Parsing.Model.Symbols;

internal record class DeferredSymbol : Symbol
{
    protected DeferredSymbol(Uri workspaceRoot, string name) : base(workspaceRoot, name)
    {
    }

    protected DeferredSymbol(Uri workspaceRoot, string name, SymbolKindExt kind, Uri? parentUri = null, ScopeKind? scope = ScopeKind.Global)
        : base(workspaceRoot, name, kind, parentUri, scope)
    {
    }

    protected DeferredSymbol(Uri workspaceRoot, ScopeKind scope, string name, SymbolKindExt kind, OmniSharp.Extensions.LanguageServer.Protocol.Models.Range range, OmniSharp.Extensions.LanguageServer.Protocol.Models.Range selectionRange, Uri? parentUri = null)
        : base(workspaceRoot, scope, name, kind, range, selectionRange, parentUri)
    {
    }
}
