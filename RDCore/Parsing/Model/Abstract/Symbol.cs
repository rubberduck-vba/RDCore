using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Parsing.Model.Abstract;


internal abstract record class Symbol : DocumentSymbol
{
    protected Symbol(Uri workspaceRoot, string name, SymbolKind kind, Uri? parentUri = default)
        : this(workspaceRoot, name, kind, default, default, parentUri) { }

    protected Symbol(Uri workspaceRoot, string name, SymbolKind kind, Range? range, Range? selectionRange, Uri? parentUri = default)
    {
        Name = name;
        Kind = kind;
        Children = [];

        Range = range!;
        SelectionRange = selectionRange ?? Range;

        ParentUri = parentUri ?? new($"lsp://symbols/{kind}");
        Uri = new Uri(ParentUri, name);
    }

    public Uri Uri { get; init; }
    public Uri ParentUri { get; init; }

    public Symbol WithChildren(IEnumerable<Symbol> children) => this with { Children = Container.From(children.OfType<DocumentSymbol>()) };
}
