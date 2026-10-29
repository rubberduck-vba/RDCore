using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Diagnostics;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Symbols;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Diagnostics;

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
/// <para>
/// An edit is followed by more as the user types: its refresh waits a moment for the next one, which supersedes it, so that a burst of typing is parsed and
/// brought to the host once. A request whose answer is of what the host has (<see cref="WhenSyncedAsync"/>) has the refresh start at once. The diagnostics
/// are found out and pushed for a client that is pushed to; one that pulls them asks for them itself, and is not given them twice over.
/// </para>
/// <para>
/// A module whose text changed what the other modules can be told by it (<see cref="ModuleSurface"/>) changes what is found out about them: once the host has
/// it, the workspace is defined again against it (<see cref="ISymbolSyncService.SyncWorkspaceAsync"/>), and the client is asked to refresh what it shows of
/// every document (<see cref="IWorkspaceRefresh"/>). The same goes for a module that is gone. An edit that changes no other module's findings - inside
/// a procedure, of a literal, a comment - does neither.
/// </para>
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

    /// <summary>
    /// A task that completes once the environment host has the text of a document as it is now, which is what its findings are to be of: a refresh that waits
    /// for more edits starts at once.
    /// </summary>
    /// <param name="documentUri">The document.</param>
    /// <param name="token">A token that cancels the wait, and not the refresh.</param>
    Task WhenSyncedAsync(Uri documentUri, CancellationToken token);
}

/// <param name="documents">The documents of the workspace.</param>
/// <param name="parsing">Parses a document.</param>
/// <param name="symbols">Tells the environment host what a document declares and what its code is.</param>
/// <param name="diagnostics">What is found of a document, for a client that is pushed to.</param>
/// <param name="publisher">Pushes what is found to the client, when it is pushed to.</param>
/// <param name="refresh">Asks the client to refresh what it shows of every document.</param>
/// <param name="logger">The log.</param>
/// <param name="editDelay">How long a refresh waits after an edit for another one, which supersedes it: a burst of typing is refreshed once. A quarter of a
/// second when not said.</param>
internal sealed class DocumentLifecycleService(
    IWorkspaceDocumentService documents,
    IParsingClientService parsing,
    ISymbolSyncService symbols,
    IDocumentDiagnosticsService diagnostics,
    IDiagnosticsPublisher publisher,
    IWorkspaceRefresh refresh,
    ILogger<DocumentLifecycleService> logger,
    TimeSpan? editDelay = null) : IDocumentLifecycleService
{
    private readonly TimeSpan _editDelay = editDelay ?? TimeSpan.FromMilliseconds(250);

    // a refresh in flight: what cancels it, what has it start without waiting for more edits, and what says the host has its text.
    private sealed record class Refresh(CancellationTokenSource Cancellation, TaskCompletionSource Hurry, TaskCompletionSource Synced)
    {
        public Task Done { get; set; } = Task.CompletedTask;
    }

    // the refresh in flight for each document: a later notification about the document supersedes it.
    private readonly ConcurrentDictionary<Uri, Refresh> _refreshes = new();

    // what the other modules were last defined against of each module, in the host, and the version of the text that was: compared, and changed, under the gate.
    private readonly Dictionary<Uri, (int Version, string? Surface)> _surfaces = [];
    private readonly object _surfaceGate = new();

    // what the other modules are defined against while a sync of them is under way, or when one did not finish: no surface, so that the next change syncs them
    // again whatever it is.
    private const string Unsynced = "\0unsynced";

    public void Opened(Uri documentUri, string text, int version)
    {
        documents.Open(documentUri, text, version);

        // the text of the client is not the text that was parsed from disk, whatever the version it comes with says: what the workspace was told by the text
        // on disk is what the client's is told apart from.
        if (parsing.TryGetCached(documentUri, out var parsed))
        {
            lock (_surfaceGate)
            {
                _surfaces[documentUri] = (0, ModuleSurface.Of(parsed));
            }
        }

        parsing.Invalidate(documentUri);
        ScheduleRefresh(documentUri, publish: true);
    }

    public DocumentChangeOutcome Changed(Uri documentUri, int version, IEnumerable<TextDocumentContentChangeEvent> changes)
    {
        var outcome = documents.Change(documentUri, version, changes);
        if (outcome == DocumentChangeOutcome.Applied)
        {
            ScheduleRefresh(documentUri, publish: true, delay: _editDelay);
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
            // they were given - is not the workspace's either, and what the other modules found of it is gone.
            await DiscardModuleAsync(moduleName);
            lock (_surfaceGate)
            {
                _surfaces.Remove(documentUri);
            }

            await SyncWorkspaceAsync(CancellationToken.None);
            refresh.Changed();
        }
    }

    // the other modules are defined again against what one declares now - what their declarations are bound to, and what their code calls - as when a module
    // changes outside the editor; whether they all were. A host that could not be told has some of them as they were.
    private async Task<bool> SyncWorkspaceAsync(CancellationToken token)
    {
        try
        {
            await symbols.SyncWorkspaceAsync(token, replace: true);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "❌ The environment host could not be brought up to date with the workspace.");
            return false;
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
        => _refreshes.TryGetValue(documentUri, out var current) ? current.Done : Task.CompletedTask;

    public Task WhenSyncedAsync(Uri documentUri, CancellationToken token)
    {
        if (!_refreshes.TryGetValue(documentUri, out var current))
        {
            return Task.CompletedTask;
        }

        current.Hurry.TrySetResult();
        return current.Synced.Task.WaitAsync(token);
    }

    private void Supersede(Uri documentUri)
    {
        if (_refreshes.TryRemove(documentUri, out var previous))
        {
            previous.Cancellation.Cancel();
            previous.Synced.TrySetResult();
        }
    }

    private void ScheduleRefresh(Uri documentUri, bool publish, TimeSpan delay = default)
    {
        Supersede(documentUri);

        var current = new Refresh(new CancellationTokenSource(), new TaskCompletionSource(), new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        _refreshes[documentUri] = current;
        current.Done = RefreshAsync(documentUri, publish, delay, current);
    }

    private async Task RefreshAsync(Uri documentUri, bool publish, TimeSpan delay, Refresh current)
    {
        // runs in the background, off the notification that scheduled it.
        await Task.Yield();
        var token = current.Cancellation.Token;

        try
        {
            // an edit is followed by more as the user types: what the next one supersedes before the wait is over is never begun. A request that needs what the
            // host has of the text does not wait for it.
            if (delay > TimeSpan.Zero)
            {
                await Task.WhenAny(Task.Delay(delay, token), current.Hurry.Task);
            }

            if (!documents.TryGetDocument(documentUri, out var document) || token.IsCancellationRequested)
            {
                return;
            }

            var version = document.Version;
            var parse = await parsing.ParseDocumentAsync(documentUri, token);
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

            current.Synced.TrySetResult();
            if (IsSuperseded(documentUri, version, token))
            {
                return;
            }

            // a client that pulls asks for what it shows itself: nothing is found out for it here, which it would only find out again.
            if (publish && publisher.IsEnabled)
            {
                // what is ready is not held back for what is not: the phases of the analysis are asked for in turn, and what each finds is published as soon as it
                // is there, together with what the earlier ones found - a publication replaces the one before it. The static findings reach the client while the
                // facts that come of evaluating the code are still being found out.
                var published = new List<Diagnostic>();
                foreach (var phase in new[] { AnalysisPhase.Static, AnalysisPhase.Runtime })
                {
                    var result = await diagnostics.GetPhaseAsync(documentUri, phase, token);
                    if (IsSuperseded(documentUri, version, token))
                    {
                        return;
                    }

                    // a phase that found nothing changes nothing, except that the first one is what clears what the text had before.
                    if (result.Diagnostics.Count > 0 || phase == AnalysisPhase.Static)
                    {
                        published.AddRange(result.Diagnostics);
                        publisher.Publish(documentUri, version, [.. published]);
                    }
                }
            }

            // what the other modules are told by this one changed: the host has them defined again against it, and then what the client shows of them is
            // refreshed. A refresh that is superseded before the sync starts leaves it to the next one, which the host's others are still as they were for.
            var surface = ModuleSurface.Of(parse);
            lock (_surfaceGate)
            {
                if (!_surfaces.TryGetValue(documentUri, out var previous))
                {
                    // a module the server has had no text of yet - opened before the workspace was parsed, or of no workspace - is what the workspace is defined
                    // with when it is: what it tells is kept, for its next change.
                    _surfaces[documentUri] = (version, surface);
                    return;
                }

                if (previous.Surface == surface || IsSuperseded(documentUri, version, token))
                {
                    return;
                }

                // the others are being defined again: until they all are, against no one surface. A refresh of a later version that compares meanwhile syncs
                // them again itself.
                _surfaces[documentUri] = (version, Unsynced);
            }

            // once a sync is under way the host is not what it was, whatever becomes of this refresh: what the others are defined against is kept, and the
            // client told. A sync that did not finish leaves them defined against no one surface, which the next change syncs again whatever it is.
            var synced = false;
            try
            {
                synced = await SyncWorkspaceAsync(token);
            }
            finally
            {
                lock (_surfaceGate)
                {
                    // a later version that has synced them since is what they are defined against.
                    if (!_surfaces.TryGetValue(documentUri, out var recorded) || recorded.Version <= version)
                    {
                        _surfaces[documentUri] = (version, synced ? surface : Unsynced);
                    }
                }

                refresh.Changed();
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
        finally
        {
            // whatever became of the refresh, nothing waits for the host any longer than it does.
            current.Synced.TrySetResult();
        }
    }

    // the text this refresh is of is not the text there is: it has been edited since, or the refresh was cancelled.
    private bool IsSuperseded(Uri documentUri, int version, CancellationToken token)
        => token.IsCancellationRequested || !documents.TryGetDocument(documentUri, out var current) || current.Version != version;
}
