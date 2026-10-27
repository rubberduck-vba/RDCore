using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.SDK.Server.Configuration;
using System.Collections.Concurrent;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;
using LspWorkspaceSymbol = OmniSharp.Extensions.LanguageServer.Protocol.Models.WorkspaceSymbol;

namespace RDCore.LanguageServer.Symbols;

/// <summary>
/// Serves <c>workspace/symbol</c> (<strong>LSP 3.17</strong> §Workspace Symbols Request): the symbols of every document of the workspace whose name matches
/// what a client asks, the closest matches first (<see cref="SymbolSearch"/>).
/// </summary>
/// <remarks>
/// A symbol is what the outline of its document says (<see cref="DocumentOutline"/>): each module, what it declares, and the members of its <c>Enum</c>s and
/// <c>Type</c>s, each named with what it is declared in (<c>containerName</c>) and located at the name it declares. The outline of a document is worked out
/// once for each text the document has. At most <see cref="Limit"/> symbols are answered: a client asks again as its user types.
/// </remarks>
internal sealed class WorkspaceSymbolHandler(
    IWorkspaceDocumentService documents,
    IParsingClientService parsing,
    ISymbolResolver resolver,
    IOptions<SdkAppOptions> options) : WorkspaceSymbolsHandlerBase
{
    /// <summary>The most symbols an answer has.</summary>
    internal const int Limit = 500;

    // the symbols of each document, by its URI, for the text they are of.
    private readonly ConcurrentDictionary<string, (string Text, IReadOnlyList<Entry> Entries)> _outlines = new(StringComparer.Ordinal);

    public override async Task<Container<LspWorkspaceSymbol>?> Handle(WorkspaceSymbolParams request, CancellationToken cancellationToken)
    {
        var entries = new List<Entry>();
        var held = new HashSet<string>(StringComparer.Ordinal);
        foreach (var document in documents.GetAllDocuments())
        {
            held.Add(document.Id.Uri.ToString());
            entries.AddRange(await EntriesOfAsync(document, cancellationToken));
        }

        // a document the workspace no longer holds has no symbols to keep.
        foreach (var gone in _outlines.Keys.Where(key => !held.Contains(key)))
        {
            _outlines.TryRemove(gone, out _);
        }

        return new Container<LspWorkspaceSymbol>(SymbolSearch.Search(request.Query ?? string.Empty, entries, entry => entry.Name, Limit)
            .Select(entry => new LspWorkspaceSymbol
            {
                Name = entry.Name,
                Kind = entry.Kind,
                ContainerName = entry.ContainerName,
                Location = new Location { Uri = entry.Uri, Range = entry.SelectionRange },
            }));
    }

    protected override WorkspaceSymbolRegistrationOptions CreateRegistrationOptions(WorkspaceSymbolCapability capability, ClientCapabilities clientCapabilities)
        => new();

    private async Task<IReadOnlyList<Entry>> EntriesOfAsync(WorkspaceDocument document, CancellationToken token)
    {
        var key = document.Id.Uri.ToString();
        if (_outlines.TryGetValue(key, out var cached) && string.Equals(cached.Text, document.Text, StringComparison.Ordinal))
        {
            return cached.Entries;
        }

        var outline = await DocumentOutline.OfAsync(document, parsing, resolver, options.Value, token);
        IReadOnlyList<Entry> entries = outline is null ? [] : [.. Flatten(outline, container: null, document.Id.Uri)];
        _outlines[key] = (document.Text, entries);
        return entries;
    }

    // the symbol and what it declares, each named with what it is declared in: the module, or the module and the Enum or Type.
    private static IEnumerable<Entry> Flatten(DocumentSymbol symbol, string? container, DocumentUri uri)
    {
        yield return new Entry(symbol.Name, symbol.Kind, container, uri, symbol.SelectionRange);
        var inner = container is null ? symbol.Name : $"{container}.{symbol.Name}";
        foreach (var child in symbol.Children ?? Enumerable.Empty<DocumentSymbol>())
        {
            foreach (var entry in Flatten(child, inner, uri))
            {
                yield return entry;
            }
        }
    }

    private sealed record class Entry(string Name, SymbolKind Kind, string? ContainerName, DocumentUri Uri, LspRange SelectionRange);
}
