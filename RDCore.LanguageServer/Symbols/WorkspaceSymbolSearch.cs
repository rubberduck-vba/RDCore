using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Services.Refactoring;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Server.Configuration;
using System.Collections.Concurrent;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.LanguageServer.Symbols;

/// <summary>
/// A symbol of the workspace as its document's outline says it (<see cref="DocumentOutline"/>): its name, its kind, what declares it, and the name it is
/// declared with.
/// </summary>
internal sealed record class WorkspaceSymbolEntry(string Name, SymbolKind Kind, string? ContainerName, DocumentUri Uri, LspRange SelectionRange);

/// <summary>
/// The symbols of every document of the workspace whose name matches a query, the closest matches first (<see cref="SymbolSearch"/>): what
/// <c>workspace/symbol</c> answers, and the assistant's <c>find_symbol</c>.
/// </summary>
internal interface IWorkspaceSymbolSearch
{
    /// <summary>
    /// The symbols whose name matches a query, the closest matches first.
    /// </summary>
    /// <param name="query">The characters a name has, in order and in any case.</param>
    /// <param name="token">Cancels the parses.</param>
    Task<IReadOnlyList<WorkspaceSymbolEntry>> SearchAsync(string query, CancellationToken token);
}

/// <inheritdoc cref="IWorkspaceSymbolSearch"/>
/// <remarks>
/// A module, what it declares, and the members of its <c>Enum</c>s and <c>Type</c>s are each a symbol, named with what they are declared in. The outline of
/// a document is worked out once for each text the document has.
/// </remarks>
internal sealed class WorkspaceSymbolSearch(
    IWorkspaceDocumentService documents,
    IParsingClientService parsing,
    ISymbolResolver resolver,
    IOptions<SdkAppOptions> options) : IWorkspaceSymbolSearch
{
    // the symbols of each document, by its URI, for the text they are of.
    private readonly ConcurrentDictionary<string, (string Text, IReadOnlyList<WorkspaceSymbolEntry> Entries)> _outlines = new(StringComparer.Ordinal);

    public async Task<IReadOnlyList<WorkspaceSymbolEntry>> SearchAsync(string query, CancellationToken token)
    {
        var entries = new List<WorkspaceSymbolEntry>();
        var held = new HashSet<string>(StringComparer.Ordinal);
        foreach (var document in documents.GetAllDocuments())
        {
            held.Add(document.Id.Uri.ToString());
            entries.AddRange(await EntriesOfAsync(document, token));
        }

        // a document the workspace no longer holds has no symbols to keep.
        foreach (var gone in _outlines.Keys.Where(key => !held.Contains(key)))
        {
            _outlines.TryRemove(gone, out _);
        }

        return SymbolSearch.Search(query, entries, entry => entry.Name, int.MaxValue);
    }

    private async Task<IReadOnlyList<WorkspaceSymbolEntry>> EntriesOfAsync(WorkspaceDocument document, CancellationToken token)
    {
        var key = document.Id.Uri.ToString();
        if (_outlines.TryGetValue(key, out var cached) && string.Equals(cached.Text, document.Text, StringComparison.Ordinal))
        {
            return cached.Entries;
        }

        var outline = await DocumentOutline.OfAsync(document, parsing, resolver, options.Value, token);
        IReadOnlyList<WorkspaceSymbolEntry> entries = [.. outline.SelectMany(module => Flatten(module, container: null, document.Id.Uri))];
        _outlines[key] = (document.Text, entries);
        return entries;
    }

    // the symbol and what it declares, each named with what it is declared in: the module, or the module and the Enum or Type.
    private static IEnumerable<WorkspaceSymbolEntry> Flatten(DocumentSymbol symbol, string? container, DocumentUri uri)
    {
        yield return new WorkspaceSymbolEntry(symbol.Name, symbol.Kind, container, uri, symbol.SelectionRange);
        var inner = container is null ? symbol.Name : $"{container}.{symbol.Name}";
        foreach (var child in symbol.Children ?? Enumerable.Empty<DocumentSymbol>())
        {
            foreach (var entry in Flatten(child, inner, uri))
            {
                yield return entry;
            }
        }
    }
}
