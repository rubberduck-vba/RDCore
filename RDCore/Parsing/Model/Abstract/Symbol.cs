using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Server.ProtocolExtensions;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Parsing.Model.Abstract;

internal abstract record class Symbol : DocumentSymbol
{
    protected Symbol(Uri workspaceRoot, string name, SymbolKindExt kind, Uri? parentUri = default)
        : this(workspaceRoot, name, kind, default, default, parentUri) { }

    protected Symbol(Uri workspaceRoot, string name, SymbolKindExt kind, Range? range, Range? selectionRange, Uri? parentUri = default)
    {
        Name = name;
        Kind = (SymbolKind)kind;
        Children = [];

        Range = range!;
        SelectionRange = selectionRange ?? Range;

        ParentUri = parentUri ?? workspaceRoot;
        Uri = new Uri(ParentUri, name);
    }

    public Uri Uri { get; init; }
    public Uri ParentUri { get; init; }

    public bool IsUserDefined => Range is not null;

    public Symbol WithChildren(IEnumerable<Symbol> children) => this with { Children = Container.From(children.OfType<DocumentSymbol>()) };
}
