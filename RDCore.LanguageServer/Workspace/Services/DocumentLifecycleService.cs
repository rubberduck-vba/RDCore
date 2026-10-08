using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Diagnostics;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Symbols;
using RDCore.SDK.Model.AST.Declarations;

namespace RDCore.LanguageServer.Workspace.Services;

/// <summary>
/// The lifecycle of a document a client has open (<strong>LSP 3.17</strong> §Text Document Synchronization): what the server does when a client opens, edits,
/// saves and closes one.
/// </summary>
/// <remarks>
/// The notification is applied to the document at once, in the order it came; what follows from it - parsing the new text, bringing the environment host up to
/// date, and publishing the diagnostics of the text - is done in the background, and never for text that has been edited since: a refresh that is superseded by
/// a later one is cancelled, and one that finds the document at another version than the one it started with stops, so that nothing is ever published for text
/// that is not there any more.
/// </remarks>
internal interface IDocumentLifecycleService
{
    /// <summary>The client opened a document.</summary>
    /// <param name="documentUri">The document.</param>
    /// <param name="text">Its text.</param>
    /// <param name="version">The version the client gives it.</param>
    void Opened(Uri documentUri, string text, int version);

    /// <summary>The client edited a document.</summary>
    /// <param name="documentUri">The document.</param>
    /// <param name="version">The version the client gives the text once edited.</param>
    /// <param name="changes">The edits.</param>
    DocumentChangeOutcome Changed(Uri documentUri, int version, IEnumerable<TextDocumentContentChangeEvent> changes);

    /// <summary>The client is about to save a document.</summary>
    /// <param name="documentUri">The document.</param>
    /// <param name="reason">Why it is saved.</param>
    void WillSave(Uri documentUri, TextDocumentSaveReason reason);

    /// <summary>The client is about to save a document, and waits for the edits the server has for it to be saved with.</summary>
    /// <param name="documentUri">The document.</param>
    /// <param name="reason">Why it is saved.</param>
    /// <param name="token">A token that cancels the request.</param>
    /// <returns>The edits to apply before the document is saved; none when it is as it should be.</returns>
    Task<IReadOnlyList<TextEdit>> WillSaveWaitUntilAsync(Uri documentUri, TextDocumentSaveReason reason, CancellationToken token);

    /// <summary>The client saved a document.</summary>
    /// <param name="documentUri">The document.</param>
    /// <param name="text">The text that was saved, when the client says what it was.</param>
    void Saved(Uri documentUri, string? text);

    /// <summary>The client closed a document.</summary>
    /// <param name="documentUri">The document.</param>
    Task ClosedAsync(Uri documentUri);

    /// <summary>
    /// A task that completes once what follows from the last notification about a document is done: parsed, synchronized with the host, and published.
    /// </summary>
    /// <param name="documentUri">The document.</param>
    Task WhenRefreshedAsync(Uri documentUri);
}

internal sealed class DocumentLifecycleService(
    IWorkspaceDocumentService documents,
    IParsingClientService parsing,
    ISymbolSyncService symbols,
    IDocumentDiagnosticsService diagnostics,
    IDiagnosticsPublisher publisher,
    ILogger<DocumentLifecycleService> logger) : IDocumentLifecycleService
{
    // the refresh in flight for each document, and what cancels it: a later notification about the document supersedes it.
    private readonly ConcurrentDictionary<Uri, (CancellationTokenSource Cancellation, Task Refresh)> _refreshes = new();

    public void Opened(Uri documentUri, string text, int version)
    {
        documents.Open(documentUri, text, version);

        // the text of the client is not the text that was parsed from disk, whatever the version it comes with says.
        parsing.Invalidate(documentUri);
        ScheduleRefresh(documentUri, publish: true);
    }

    public DocumentChangeOutcome Changed(Uri documentUri, int version, IEnumerable<TextDocumentContentChangeEvent> changes)
    {
        var outcome = documents.Change(documentUri, version, changes);
        if (outcome == DocumentChangeOutcome.Applied)
        {
            ScheduleRefresh(documentUri, publish: true);
        }

        return outcome;
    }

    public void WillSave(Uri documentUri, TextDocumentSaveReason reason)
        => logger.LogInformation("💾 {uri} will be saved ({reason}).", documentUri, reason);

    public Task<IReadOnlyList<TextEdit>> WillSaveWaitUntilAsync(Uri documentUri, TextDocumentSaveReason reason, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        // nothing in the platform rewrites a document before it is saved yet: it is as it should be.
        logger.LogInformation("💾 {uri} will be saved ({reason}); no edits to make.", documentUri, reason);
        return Task.FromResult<IReadOnlyList<TextEdit>>([]);
    }

    public void Saved(Uri documentUri, string? text)
    {
        if (!documents.Saved(documentUri, text))
        {
            logger.LogWarning("🐛 {uri} was saved, but the document is not open.", documentUri);
            return;
        }

        // what is saved is what is on disk, and so what the rest of the workspace is built on.
        ScheduleRefresh(documentUri, publish: true);
    }

    public async Task ClosedAsync(Uri documentUri)
    {
        Supersede(documentUri);

        // the name the host has the module under is read off the text it was defined from, which is gone once the document is.
        var moduleName = ModuleNameOf(documentUri);

        var document = await documents.CloseAsync(documentUri);
        parsing.Invalidate(documentUri);

        // a document that is closed has nothing wrong with it to say: what is published for it is cleared. What is on disk, when there is a file, is what the
        // workspace is built on again.
        publisher.Clear(documentUri);
        if (document is not null)
        {
            ScheduleRefresh(documentUri, publish: false);
        }
        else if (moduleName is not null)
        {
            // the workspace has no document of it any more, and so no module: what the host holds of it - the variables its lines declared, and the storage
            // they were given - is not the workspace's either.
            await DiscardModuleAsync(moduleName);
        }
    }

    private string? ModuleNameOf(Uri documentUri)
        => documents.TryGetDocument(documentUri, out var document) && parsing.TryGetCached(documentUri, out var parse) && parse.SyntaxTree is { } tree
            ? tree.GetDeclaredName() ?? document.Name
            : null;

    private async Task DiscardModuleAsync(string moduleName)
    {
        try
        {
            await symbols.DiscardModuleAsync(moduleName, CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // a failure to tell the host is not a failure to close: the document is closed.
            logger.LogWarning(exception, "🗑️ The host could not be told that module {module} is gone.", moduleName);
        }
    }

    public Task WhenRefreshedAsync(Uri documentUri)
        => _refreshes.TryGetValue(documentUri, out var current) ? current.Refresh : Task.CompletedTask;

    private void Supersede(Uri documentUri)
    {
        if (_refreshes.TryRemove(documentUri, out var previous))
        {
            previous.Cancellation.Cancel();
        }
    }

    private void ScheduleRefresh(Uri documentUri, bool publish)
    {
        Supersede(documentUri);

        var cancellation = new CancellationTokenSource();
        var refresh = RefreshAsync(documentUri, publish, cancellation.Token);
        _refreshes[documentUri] = (cancellation, refresh);
    }

    private async Task RefreshAsync(Uri documentUri, bool publish, CancellationToken token)
    {
        // runs in the background, off the notification that scheduled it.
        await Task.Yield();

        try
        {
            if (!documents.TryGetDocument(documentUri, out var document))
            {
                return;
            }

            var version = document.Version;
            await parsing.ParseDocumentAsync(documentUri, token);
            if (IsSuperseded(documentUri, version, token))
            {
                return;
            }

            // the host is told what the text says now, so that what it found out is of it.
            try
            {
                await symbols.SyncDocumentAsync(documentUri, token);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "❌ The environment host could not be brought up to date with {uri} v{version}.", documentUri, version);
            }

            if (!publish || IsSuperseded(documentUri, version, token))
            {
                return;
            }

            var result = await diagnostics.GetAsync(documentUri, previousResultId: null, token);
            if (!IsSuperseded(documentUri, version, token))
            {
                publisher.Publish(documentUri, version, result.Diagnostics);
            }
        }
        catch (OperationCanceledException)
        {
            // superseded, or the server is shutting down: either way, nothing is left to do for this version.
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "❌ Refreshing {uri} failed.", documentUri);
        }
    }

    // the text this refresh is of is not the text there is: it has been edited since, or the refresh was cancelled.
    private bool IsSuperseded(Uri documentUri, int version, CancellationToken token)
        => token.IsCancellationRequested || !documents.TryGetDocument(documentUri, out var current) || current.Version != version;
}
