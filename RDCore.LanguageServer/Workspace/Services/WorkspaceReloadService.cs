using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace.States;
using RDCore.SDK.Client;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server.Services.States;
using RDCore.SDK.Workspace;
using System.Text.Json;

namespace RDCore.LanguageServer.Workspace.Services;

/// <summary>
/// Makes the workspace what its project file lists now, once a client said the <c>.rdproj</c> changed: the files it lists that were not loaded are loaded and
/// parsed, the ones it no longer lists are taken out, and the whole workspace is defined again in the environment host, in its three phases.
/// </summary>
/// <remarks>
/// The project file is read when the server initializes (<see cref="IWorkspaceService.LoadAsync"/>); a client that rewrites it while the server runs says so with
/// <c>workspace/didChangeWatchedFiles</c>, and this is what is done about it. The client owns the file system: the server is told, and watches only for a client
/// that does not say it watches (<see cref="Server.Handlers.Workspace.IWorkspaceFileSystemWatcher"/>).
/// <para>
/// The client is told of a workspace that is not what it was (<see cref="IWorkspaceRefresh"/>): what it shows of each document may be of what changed.
/// </para>
/// <para>
/// The server writes the project file itself when the client creates, renames or deletes a file of the workspace (<see cref="ChangeAsync"/>): it is the
/// project file's only writer. A client that watches the project file then says it changed, and reading it again changes nothing.
/// </para>
/// </remarks>
internal interface IWorkspaceReloadService
{
    /// <summary>
    /// Reads the project file again, and makes the workspace what it lists.
    /// </summary>
    /// <param name="token">A token that cancels the reload.</param>
    /// <returns>
    /// What the reload changed - <see cref="WorkspaceReloadResult.Unchanged"/> when the project file lists what the workspace is, as when the server wrote it -
    /// or <see langword="null"/> when the server is not running a workspace it could reload.
    /// </returns>
    /// <exception cref="InvalidDataException">The project file is not valid; the workspace is left as it was.</exception>
    Task<WorkspaceReloadResult?> ReloadAsync(CancellationToken token);

    /// <summary>
    /// Reads the project file again as <see cref="ReloadAsync"/> does, and returns once the workspace's documents are what it lists: the environment host and the symbols
    /// follow, as for <see cref="BeginChangeAsync"/>.
    /// </summary>
    /// <param name="token">A token that cancels the reload before the documents are made.</param>
    /// <returns>The reload, and when it is done; <see langword="null"/> when the server is not running a workspace it could reload.</returns>
    /// <exception cref="InvalidDataException">The project file is not valid; the workspace is left as it was.</exception>
    Task<WorkspaceReloadStep?> BeginReloadAsync(CancellationToken token);

    /// <summary>
    /// Changes the project, writes the project file, and makes the workspace what it lists.
    /// </summary>
    /// <remarks>
    /// A project the server made up for a folder with no project file (<see cref="ProjectFile.IsSynthesized"/>) is written on its first change, and is a
    /// project file from then on.
    /// </remarks>
    /// <param name="change">What the project is to be, from what it is.</param>
    /// <param name="token">A token that cancels the change.</param>
    /// <returns>
    /// What the change made of the workspace - <see cref="WorkspaceReloadResult.Unchanged"/>, and nothing written, when the project is what it was - or
    /// <see langword="null"/> when the server is not running a workspace it could change.
    /// </returns>
    /// <exception cref="ProjectFileNotWrittenException">The file system would not let the project file be written; the project is what it was.</exception>
    /// <exception cref="IOException">The project file was written, and a file it lists could not be read.</exception>
    Task<WorkspaceReloadResult?> ChangeAsync(Func<ProjectFile, ProjectFile> change, CancellationToken token);

    /// <summary>
    /// Changes the project as <see cref="ChangeAsync"/> does (and, as it does, throws <see cref="ProjectFileNotWrittenException"/> when the file system would not let the
    /// project file be written), and returns once the project file is written and the workspace's documents are what it lists: the
    /// environment host and the symbols follow, in the order the changes were made, with no one waiting for them.
    /// </summary>
    /// <remarks>
    /// A host that is not up, or is starting, does not hold up the next change, nor a client's notifications that are read one after another: they are not waiting
    /// for it. What is left to do is cancelled by <see cref="Stop"/>.
    /// </remarks>
    /// <param name="change">What the project is to be, from what it is.</param>
    /// <param name="token">A token that cancels the change before the project file is written.</param>
    /// <returns>The change, and when it is done; <see langword="null"/> when the server is not running a workspace it could change.</returns>
    Task<WorkspaceReloadStep?> BeginChangeAsync(Func<ProjectFile, ProjectFile> change, CancellationToken token);

    /// <summary>
    /// Cancels what is left to do of the changes made so far, and of those that are made from now on: the server is going away.
    /// </summary>
    void Stop();

    /// <summary>
    /// A task that completes once no change is being made: the documents of each change read, and the environment host and the symbols what they list - those of a
    /// change made while it waits included. What a program that waits is checked against is the workspace once it is.
    /// </summary>
    /// <param name="token">A token that cancels the wait, and not the changes.</param>
    Task WhenIdleAsync(CancellationToken token);

    /// <summary>
    /// Reads again the files of the project that changed outside the editor - a <c>git checkout</c>, a module exported from the VBE - and defines the workspace
    /// again when one of them is not what was loaded.
    /// </summary>
    /// <remarks>
    /// Only a file the project lists is read: the project file says what the workspace is. A document the client has open is the client's, and is not read. A
    /// file whose text is what was loaded is nothing changed, as when the server wrote it. A module whose file is gone is taken out of the environment host,
    /// and its document is missing until the project file no longer lists it.
    /// </remarks>
    /// <param name="files">The files that changed, created or were deleted.</param>
    /// <param name="token">A token that cancels the reload.</param>
    /// <returns>
    /// What was read again - <see cref="WorkspaceReloadResult.Unchanged"/> when no file is other than it was - or <see langword="null"/> when the server is not
    /// running a workspace.
    /// </returns>
    Task<WorkspaceReloadResult?> ReloadFilesAsync(IReadOnlyList<Uri> files, CancellationToken token);

    /// <summary>
    /// Reads the files again as <see cref="ReloadFilesAsync"/> does, and returns once their documents are read: the environment host and the symbols follow, as for
    /// <see cref="BeginChangeAsync"/>.
    /// </summary>
    /// <param name="files">The files that changed, created or were deleted.</param>
    /// <param name="token">A token that cancels the reload before the documents are read.</param>
    /// <returns>The reload, and when it is done; <see langword="null"/> when the server is not running a workspace.</returns>
    Task<WorkspaceReloadStep?> BeginReloadFilesAsync(IReadOnlyList<Uri> files, CancellationToken token);
}

/// <summary>
/// A change of the project that was made: the documents are what the project lists, and the host and the symbols follow.
/// </summary>
/// <param name="Started">What the change made of the documents: <see cref="WorkspaceReloadResult.IsHostComposed"/> is not known yet.</param>
/// <param name="Completed">Completes with what the change made of the workspace once the host composed its session and the symbols were defined; it is cancelled with the server.</param>
internal sealed record class WorkspaceReloadStep(WorkspaceReloadResult Started, Task<WorkspaceReloadResult> Completed);

/// <summary>
/// The file system would not let the project file be written (<see cref="IWorkspaceReloadService.ChangeAsync"/>): what it threw is the inner exception. Told
/// apart from a failure to read the files the project lists once it is written, which is another refusal.
/// </summary>
internal sealed class ProjectFileNotWrittenException(Exception cause) : IOException(cause.Message, cause);

/// <summary>
/// What reloading the project file changed.
/// </summary>
/// <param name="Added">The files the project lists now that it did not.</param>
/// <param name="Removed">The files it no longer lists.</param>
/// <param name="IsHostComposed">Whether the environment host composed its runtime session again from the project file (<see cref="ProjectReload"/>).</param>
/// <param name="IsProgramRunning">Whether the host kept its session, a program running or waiting in it.</param>
/// <param name="Reread">The files of the project that were read again, changed outside the editor.</param>
internal sealed record class WorkspaceReloadResult(int Added, int Removed, bool IsHostComposed, bool IsProgramRunning, int Reread = 0)
{
    /// <summary>The project is what it was: nothing was loaded, taken out, or defined again.</summary>
    public static WorkspaceReloadResult Unchanged { get; } = new(0, 0, false, false) { IsUnchanged = true };

    /// <summary>Whether the project is what it was, and nothing was done.</summary>
    public bool IsUnchanged { get; private init; }
}

internal sealed class WorkspaceReloadService(
    IServerStateProvider serverStateProvider,
    IProjectFileService projectFiles,
    IWorkspaceDocumentService documents,
    IDocumentStateProvider documentStates,
    IParsingClientService parsing,
    ISymbolSyncService symbols,
    IPlatformOrchestrationService orchestration,
    System.IO.Abstractions.IPath ioPath,
    IWorkspaceRefresh refresh,
    ILogger<WorkspaceReloadService> logger,
    RDCore.LanguageServer.Debugging.IEditStatusMonitor? editStatus = null) : IWorkspaceReloadService
{
    // one reload at a time: a client that writes the project file twice in a row is told about it twice, and the second reload reads what the second write
    // left.
    private readonly SemaphoreSlim _reloading = new(1, 1);

    // The environment host and the symbols follow the documents, in the order the changes were made, and not under the lock of the changes: the host may be down or
    // starting, and a change that is waiting for it is no reason for the next to wait before it has the documents it lists. What is left to do of the changes that wait
    // is done once for all of them: each ends in a full definition of the workspace, which the one after it would only do again.
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _queue = new();
    private readonly List<Pending> _waiting = [];
    private bool _draining;
    private Task _drained = Task.CompletedTask;

    /// <inheritdoc/>
    public async Task WhenIdleAsync(CancellationToken token)
    {
        while (true)
        {
            // a change whose documents are being read has its work queued once they are: the lock of the changes is taken once it is let go.
            await _reloading.WaitAsync(token);
            _reloading.Release();

            Task drained;
            lock (_queue)
            {
                if (!_draining)
                {
                    return;
                }

                drained = _drained;
            }

            // a change made while the batch was done is done after it: the wait goes on until none is left.
            await drained.WaitAsync(token);
        }
    }

    /// <inheritdoc/>
    public void Stop()
    {
        // under the lock of the queue, with the last check of a batch and the completing of its changes: a change is done before the server stopped, or not at all.
        lock (_queue)
        {
            _lifetime.Cancel();
        }
    }

    public async Task<WorkspaceReloadResult?> ReloadAsync(CancellationToken token)
        => await BeginReloadAsync(token) is { } step ? await step.Completed.WaitAsync(token) : null;

    public async Task<WorkspaceReloadStep?> BeginReloadAsync(CancellationToken token)
    {
        if (!IsRunningAWorkspace())
        {
            logger.LogInformation("The project file changed before a workspace was loaded; it is read when the workspace is.");
            return null;
        }

        await _reloading.WaitAsync(token);
        try
        {
            var project = projectFiles.Project;

            // a project file that is not valid leaves the workspace as it was: the loader throws, and nothing was changed yet.
            await projectFiles.LoadAsync(project.Uri);
            if (IsTheSame(project, projectFiles.Project))
            {
                logger.LogTrace("The project file lists what the workspace is; nothing is done.");
                return new WorkspaceReloadStep(WorkspaceReloadResult.Unchanged, Task.FromResult(WorkspaceReloadResult.Unchanged));
            }

            // queued before the next change takes the lock: what is left to do is done in the order the changes were made.
            return Queued(await ApplyDocumentsAsync(project, token));
        }
        finally
        {
            _reloading.Release();
        }
    }


    public async Task<WorkspaceReloadResult?> ChangeAsync(Func<ProjectFile, ProjectFile> change, CancellationToken token)
        => await BeginChangeAsync(change, token) is { } step ? await step.Completed.WaitAsync(token) : null;

    public async Task<WorkspaceReloadStep?> BeginChangeAsync(Func<ProjectFile, ProjectFile> change, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (!IsRunningAWorkspace())
        {
            logger.LogInformation("The workspace changed before it was loaded; what the project lists is read when it is.");
            return null;
        }

        await _reloading.WaitAsync(token);
        try
        {
            var project = projectFiles.Project;
            var changed = change(project);
            if (IsTheSame(project, changed))
            {
                return new WorkspaceReloadStep(WorkspaceReloadResult.Unchanged, Task.FromResult(WorkspaceReloadResult.Unchanged));
            }

            // the environment host reads the project file to compose its session, so it is written first. A project file that cannot be written leaves the
            // project what it was - what is on disk, and what the workspace is - rather than one that is neither.
            projectFiles.Update(changed);
            try
            {
                await projectFiles.SaveAsync();
            }
            catch (Exception exception)
            {
                projectFiles.Update(project);
                if (exception is UnauthorizedAccessException or IOException)
                {
                    throw new ProjectFileNotWrittenException(exception);
                }

                throw;
            }

            return Queued(await ApplyDocumentsAsync(project, token));
        }
        finally
        {
            _reloading.Release();
        }
    }

    public async Task<WorkspaceReloadResult?> ReloadFilesAsync(IReadOnlyList<Uri> files, CancellationToken token)
        => await BeginReloadFilesAsync(files, token) is { } step ? await step.Completed.WaitAsync(token) : null;

    // running, whatever the trace a client asked for: a server that is traced nothing is RunningTraceless, and still running.
    public async Task<WorkspaceReloadStep?> BeginReloadFilesAsync(IReadOnlyList<Uri> files, CancellationToken token)
    {
        if (!IsRunningAWorkspace())
        {
            logger.LogInformation("Files changed before a workspace was loaded; they are read when the workspace is.");
            return null;
        }

        await _reloading.WaitAsync(token);
        try
        {
            return Queued(await ReloadListedFilesAsync(files, token));
        }
        finally
        {
            _reloading.Release();
        }
    }

    // What is left to do is queued by whoever made the change, before the lock of the changes is released: the order of the queue is the order of the changes.
    private WorkspaceReloadStep Queued(Applied applied)
    {
        if (applied.Work is null)
        {
            return new WorkspaceReloadStep(applied.Started, Task.FromResult(applied.Started));
        }

        var pending = new Pending(applied.Work);

        // whoever stops waiting for it, a fault of the work is observed all the same.
        _ = pending.Done.Task.ContinueWith(done => done.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        lock (_queue)
        {
            if (_lifetime.IsCancellationRequested)
            {
                pending.Done.TrySetCanceled(_lifetime.Token);
            }
            else
            {
                _waiting.Add(pending);
                if (!_draining)
                {
                    _draining = true;
                    _drained = Task.Run(DrainAsync);
                }
            }
        }

        return new WorkspaceReloadStep(applied.Started, pending.Done.Task);
    }

    // One batch at a time: what came while the last was waiting for the host is done with it, and what came while it was working is the next.
    private async Task DrainAsync()
    {
        while (true)
        {
            List<Pending> batch;
            lock (_queue)
            {
                if (_waiting.Count == 0)
                {
                    _draining = false;
                    return;
                }

                batch = [.. _waiting];
                _waiting.Clear();
            }

            try
            {
                await FinishAsync(batch, _lifetime.Token);
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
                // only the server stopping cancels what the changes left to do.
                foreach (var pending in batch)
                {
                    pending.Done.TrySetCanceled(_lifetime.Token);
                }
            }
            catch (Exception exception)
            {
                // a request that was cancelled for another reason - the connection to the host was lost - is a failure like any other.
                logger.LogError(exception, "❌ The environment host and the symbols could not be made what the project lists.");

                // the documents of the batch were made all the same: what the client shows of the others is of a workspace that is not what it was.
                lock (_queue)
                {
                    if (!_lifetime.IsCancellationRequested)
                    {
                        refresh.Changed();
                    }

                    foreach (var pending in batch)
                    {
                        pending.Done.TrySetException(exception);
                    }
                }
            }
        }
    }

    // The batch is done when the server is not stopping: once it is, nothing is reported, whatever was left to do.
    private async Task FinishAsync(List<Pending> batch, CancellationToken stopping)
    {
        stopping.ThrowIfCancellationRequested();
        if (batch.Any(pending => pending.Work.ComposeHost) && orchestration.RuntimeEnvironment is { } host)
        {
            await host.WaitForReadyAsync(stopping);
        }

        // what was made while the host was not ready is done with this batch.
        lock (_queue)
        {
            batch.AddRange(_waiting);
            _waiting.Clear();
        }

        stopping.ThrowIfCancellationRequested();
        var work = batch.Select(pending => pending.Work).ToList();

        // a module that cannot be parsed is left out and is the failure of the change that gained it: the others are made all the same, and the workspace is defined.
        var failed = new Dictionary<TextDocumentIdentifier, Exception>();
        foreach (var id in work.SelectMany(each => each.ToParse).Distinct())
        {
            stopping.ThrowIfCancellationRequested();
            if (!documents.TryGetDocument(id.Uri.ToUri(), out _))
            {
                continue;
            }

            try
            {
                await parsing.ParseDocumentAsync(id.Uri.ToUri(), stopping);
            }
            catch (Exception exception) when (!stopping.IsCancellationRequested)
            {
                logger.LogWarning(exception, "⚠️ '{uri}' could not be parsed; it is left out, and the workspace is defined without it.", id.Uri);
                failed[id] = exception;
            }
        }

        var (composed, programRunning) = work.Any(each => each.ComposeHost) ? await ComposeHostAsync(stopping) : (false, false);
        if (composed)
        {
            symbols.SessionComposedAnew();
        }

        foreach (var module in work.SelectMany(each => each.ToDiscard).Distinct())
        {
            await symbols.DiscardModuleAsync(module, stopping);
        }

        // a session composed again holds nothing of what was defined in it; one that was kept still holds what the removed modules declared.
        if (!composed)
        {
            foreach (var module in work.SelectMany(each => each.RemovedModules).Distinct())
            {
                await symbols.DiscardModuleAsync(module, stopping);
            }
        }

        // every module is defined again, in the three phases: a class the project gained is a type another module's declarations name.
        await symbols.SyncWorkspaceAsync(stopping, progress: null, replace: true);

        // a program that waits may not have what the batch changed: it is said, before the batch is done.
        if (editStatus is not null)
        {
            await editStatus.CheckAsync(stopping, afterLoad: true);
        }

        // the definition of the symbols ends quietly when the server stops: it is not a definition that was made. The last check and the completing are one step with
        // Stop, so that nothing is reported after the server stopped.
        lock (_queue)
        {
            stopping.ThrowIfCancellationRequested();

            // the client is told of a workspace that is not what it was once it is: the host and the symbols are what the documents are, once for all the changes of the
            // batch, and before any of them is done.
            refresh.Changed();
            foreach (var pending in batch)
            {
                var failure = pending.Work.ToParse.Select(id => failed.GetValueOrDefault(id)).FirstOrDefault(exception => exception is not null);
                if (failure is not null)
                {
                    pending.Done.TrySetException(failure);
                }
                else
                {
                    pending.Done.TrySetResult(pending.Work.Result(composed, programRunning));
                }
            }
        }
    }

    private async Task<Applied> ReloadListedFilesAsync(IReadOnlyList<Uri> files, CancellationToken token)
    {
        var root = projectFiles.Project.Uri;
        var listed = FilesOf(projectFiles.Project)
            .ToDictionary(file => file.Key.Replace('\\', '/'), file => file.Value, StringComparer.OrdinalIgnoreCase);

        var reread = 0;
        var toParse = new List<TextDocumentIdentifier>();
        var toDiscard = new List<string>();
        foreach (var file in files.Where(file => file.IsFile).Distinct())
        {
            token.ThrowIfCancellationRequested();
            var relativePath = ioPath.GetRelativePath(root, file.LocalPath).Replace('\\', '/');
            if (!listed.TryGetValue(relativePath, out var isModule))
            {
                continue;
            }

            var id = IdOf(root, relativePath);
            if (documentStates.IsTracked(id) && documentStates.GetCurrentState(id) is OpenedDocumentState)
            {
                continue;
            }

            var before = documents.TryGetDocument(id.Uri.ToUri(), out var document) ? document : null;
            var moduleName = before is not null && parsing.TryGetCached(id.Uri.ToUri(), out var parse) && parse.SyntaxTree?.GetDeclaredName() is { } name
                ? name
                : before?.Name;

            // a document is loaded from an Unloaded state only: whatever it was - loaded, missing, unreadable - it is read anew, unless the client opened it meanwhile.
            var wasLoaded = documentStates.IsTracked(id) && documentStates.GetCurrentState(id) is LoadedDocumentState;
            if (!documents.Reset(id))
            {
                continue;
            }

            var loaded = await documents.TryLoadAsync(id);
            if (documentStates.IsTracked(id) && documentStates.GetCurrentState(id) is OpenedDocumentState)
            {
                continue;
            }

            var after = documents.TryGetDocument(id.Uri.ToUri(), out document) ? document : null;
            if (loaded == wasLoaded && string.Equals(before?.Text, after?.Text, StringComparison.Ordinal))
            {
                continue;
            }

            reread++;
            if (!loaded)
            {
                // the module's file is gone: what the host holds of it goes with it.
                if (isModule && moduleName is not null)
                {
                    toDiscard.Add(moduleName);
                }

                continue;
            }

            if (isModule)
            {
                toParse.Add(id);
            }
        }

        if (reread == 0)
        {
            logger.LogTrace("The files that changed are what was loaded; nothing is done.");
            return new Applied(WorkspaceReloadResult.Unchanged, null);
        }

        var started = new WorkspaceReloadResult(0, 0, false, false, reread);
        return new Applied(started, new FinishWork(toParse, toDiscard, [], ComposeHost: false, (_, _) =>
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("🔄 {reread} file(s) of the project changed outside the editor, and were read again.", reread);
            }

            return started;
        }));
    }

    private bool IsRunningAWorkspace() => serverStateProvider.State is RunningServerState && projectFiles.Project?.ProjectInfo is not null;

    // what a project file would hold: the same text is the same project.
    private static bool IsTheSame(ProjectFile project, ProjectFile other)
        => string.Equals(JsonSerializer.Serialize(project), JsonSerializer.Serialize(other), StringComparison.Ordinal);

    // The documents are made what the project lists: the files it gained are read, those it lost are taken out. What is left to do is the host's and the symbols' - the
    // environment host composes its session, and every module is defined again.
    private async Task<Applied> ApplyDocumentsAsync(ProjectFile project, CancellationToken token)
    {
        var root = project.Uri;
        var before = FilesOf(project);
        var after = FilesOf(projectFiles.Project);

        // what the project now lists as other files is no code, whatever it was before.
        documents.SetOtherFiles(after.Where(file => !file.Value).Select(file => IdOf(projectFiles.Project.Uri, file.Key)));

        // and what it says its document modules are: a module whose Super changed is the kind it says now, when every module is defined again.
        documents.SetDocumentModules(projectFiles.Project.Uri, projectFiles.Project.ProjectInfo.Modules.Where(module => module.Super is not null)
            .Select(module => KeyValuePair.Create(IdOf(projectFiles.Project.Uri, module.RelativeUri), module.Super!.Value)));

        var removed = before.Keys.Where(path => !after.ContainsKey(path)).ToList();
        var added = after.Keys.Where(path => !before.ContainsKey(path)).ToList();

        // the name each removed module was defined under, which is read from what was parsed of it, before it goes.
        var removedModules = new List<string>();
        foreach (var path in removed)
        {
            var id = IdOf(root, path);
            if (before[path] && documents.TryGetDocument(id.Uri.ToUri(), out var document))
            {
                removedModules.Add(parsing.TryGetCached(id.Uri.ToUri(), out var parse) && parse.SyntaxTree?.GetDeclaredName() is { } name ? name : document.Name);
            }

            documents.Remove(id);
        }

        var toParse = new List<TextDocumentIdentifier>();
        foreach (var path in added)
        {
            token.ThrowIfCancellationRequested();
            var id = IdOf(root, path);
            documentStates.Track(id);

            // a document the client has open is the client's, and was parsed when it was opened: what is on disk is not its text.
            if (documentStates.GetCurrentState(id) is OpenedDocumentState)
            {
                continue;
            }

            if (await documents.TryLoadAsync(id) && after[path])
            {
                toParse.Add(id);
            }
        }

        var started = new WorkspaceReloadResult(added.Count, removed.Count, false, false);
        return new Applied(started, new FinishWork(toParse, [], removedModules, ComposeHost: true, (composed, programRunning) =>
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("🔄 Project file reloaded: {added} file(s) added, {removed} removed; the environment host {host}.",
                    added.Count, removed.Count, composed ? "composed its session again" : programRunning ? "kept its session, a program runs" : "was not asked to compose");
            }

            return new WorkspaceReloadResult(added.Count, removed.Count, composed, programRunning);
        }));
    }

    // What is left to do once the documents are what the project lists: the documents to parse, the modules the host forgets, whether the host composes its session
    // again, and what the change made of the workspace once that is done.
    private sealed record class FinishWork(
        IReadOnlyList<TextDocumentIdentifier> ToParse,
        IReadOnlyList<string> ToDiscard,
        IReadOnlyList<string> RemovedModules,
        bool ComposeHost,
        Func<bool, bool, WorkspaceReloadResult> Result);

    private sealed record class Applied(WorkspaceReloadResult Started, FinishWork? Work);

    private sealed class Pending(FinishWork work)
    {
        public FinishWork Work { get; } = work;

        public TaskCompletionSource<WorkspaceReloadResult> Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    // the environment host composes its session from the project file, and a class module's symbol is the project's: it composes it again, unless a program
    // runs in it.
    private async Task<(bool Composed, bool ProgramRunning)> ComposeHostAsync(CancellationToken token)
    {
        var host = orchestration.RuntimeEnvironment;
        if (host is null)
        {
            return (false, false);
        }

        await host.WaitForReadyAsync(token);
        if (host.PlatformInfo?.Provides<ProjectReload>() != true)
        {
            logger.LogWarning("The environment host does not provide the ProjectReload capability; a class the project gained is not a type until it restarts.");
            return (false, false);
        }

        var result = await host.SendRequestAsync<HostProjectReloadParams, HostProjectReloadResult>(
            new HostProjectReloadParams { WorkspaceRoot = new Uri(documents.WorkspaceRoot) }, token);
        return (result.Composed, result.IsProgramRunning);
    }

    // what the project lists, by the path relative to the workspace root it is listed under, and whether it is a module (a source file) or another file.
    private static Dictionary<string, bool> FilesOf(ProjectFile project)
    {
        var files = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in project.ProjectInfo.Modules)
        {
            files[module.RelativeUri] = true;
        }

        foreach (var file in project.ProjectInfo.OtherFiles)
        {
            files.TryAdd(file.RelativeUri, false);
        }

        return files;
    }

    // a path that climbs out of the project's folder (../Shared/Util.bas) is the file it names: the address the document has.
    private TextDocumentIdentifier IdOf(string root, string relativePath)
        => new(WorkspacePaths.Climbs(relativePath) ? ioPath.GetFullPath(ioPath.Combine(root, relativePath)) : ioPath.Combine(root, relativePath));
}
