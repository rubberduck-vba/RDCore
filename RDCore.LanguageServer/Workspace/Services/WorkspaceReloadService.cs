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
    Task<WorkspaceReloadResult?> ChangeAsync(Func<ProjectFile, ProjectFile> change, CancellationToken token);

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
}

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
    ILogger<WorkspaceReloadService> logger) : IWorkspaceReloadService
{
    // one reload at a time: a client that writes the project file twice in a row is told about it twice, and the second reload reads what the second write
    // left.
    private readonly SemaphoreSlim _reloading = new(1, 1);

    public async Task<WorkspaceReloadResult?> ReloadAsync(CancellationToken token)
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
                return WorkspaceReloadResult.Unchanged;
            }

            return Told(await ApplyAsync(project, token));
        }
        finally
        {
            _reloading.Release();
        }
    }

    // the client is told of a workspace that is not what it was.
    private WorkspaceReloadResult Told(WorkspaceReloadResult result)
    {
        if (!result.IsUnchanged)
        {
            refresh.Changed();
        }

        return result;
    }

    public async Task<WorkspaceReloadResult?> ChangeAsync(Func<ProjectFile, ProjectFile> change, CancellationToken token)
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
                return WorkspaceReloadResult.Unchanged;
            }

            // the environment host reads the project file to compose its session, so it is written first.
            projectFiles.Update(changed);
            await projectFiles.SaveAsync();
            return Told(await ApplyAsync(project, token));
        }
        finally
        {
            _reloading.Release();
        }
    }

    // running, whatever the trace a client asked for: a server that is traced nothing is RunningTraceless, and still running.
    public async Task<WorkspaceReloadResult?> ReloadFilesAsync(IReadOnlyList<Uri> files, CancellationToken token)
    {
        if (!IsRunningAWorkspace())
        {
            logger.LogInformation("Files changed before a workspace was loaded; they are read when the workspace is.");
            return null;
        }

        await _reloading.WaitAsync(token);
        try
        {
            return Told(await ReloadListedFilesAsync(files, token));
        }
        finally
        {
            _reloading.Release();
        }
    }

    private async Task<WorkspaceReloadResult> ReloadListedFilesAsync(IReadOnlyList<Uri> files, CancellationToken token)
    {
        var root = projectFiles.Project.Uri;
        var listed = FilesOf(projectFiles.Project)
            .ToDictionary(file => file.Key.Replace('\\', '/'), file => file.Value, StringComparer.OrdinalIgnoreCase);

        var reread = 0;
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

            // a document is loaded from an Unloaded state only: whatever it was - loaded, missing, unreadable - it is read anew.
            var wasLoaded = documentStates.IsTracked(id) && documentStates.GetCurrentState(id) is LoadedDocumentState;
            documentStates.Forget(id);
            documentStates.Track(id);
            var loaded = await documents.TryLoadAsync(id);
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
                    await symbols.DiscardModuleAsync(moduleName, token);
                }

                continue;
            }

            if (isModule)
            {
                await parsing.ParseDocumentAsync(id.Uri.ToUri(), token);
            }
        }

        if (reread == 0)
        {
            logger.LogTrace("The files that changed are what was loaded; nothing is done.");
            return WorkspaceReloadResult.Unchanged;
        }

        // every module is defined again: what another module refers to in the one that changed is bound again too.
        await symbols.SyncWorkspaceAsync(token, progress: null, replace: true);
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("🔄 {reread} file(s) of the project changed outside the editor, and were read again.", reread);
        }

        return new WorkspaceReloadResult(0, 0, false, false, reread);
    }

    private bool IsRunningAWorkspace() => serverStateProvider.State is RunningServerState && projectFiles.Project?.ProjectInfo is not null;

    // what a project file would hold: the same text is the same project.
    private static bool IsTheSame(ProjectFile project, ProjectFile other)
        => string.Equals(JsonSerializer.Serialize(project), JsonSerializer.Serialize(other), StringComparison.Ordinal);

    // the workspace was what the project was; it is made what the project is now.
    private async Task<WorkspaceReloadResult> ApplyAsync(ProjectFile project, CancellationToken token)
    {
        var root = project.Uri;
        var before = FilesOf(project);
        var after = FilesOf(projectFiles.Project);

        // what the project now lists as other files is no code, whatever it was before.
        documents.SetOtherFiles(after.Where(file => !file.Value).Select(file => IdOf(projectFiles.Project.Uri, file.Key)));

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
                await parsing.ParseDocumentAsync(id.Uri.ToUri(), token);
            }
        }

        var (composed, programRunning) = await ComposeHostAsync(root, token);

        // a session composed again holds nothing of what was defined in it; one that was kept still holds what the removed modules declared.
        if (!composed)
        {
            foreach (var module in removedModules)
            {
                await symbols.DiscardModuleAsync(module, token);
            }
        }

        // every module is defined again, in the three phases: a class the project gained is a type another module's declarations name.
        await symbols.SyncWorkspaceAsync(token, progress: null, replace: true);

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("🔄 Project file reloaded: {added} file(s) added, {removed} removed; the environment host {host}.",
                added.Count, removed.Count, composed ? "composed its session again" : programRunning ? "kept its session, a program runs" : "was not asked to compose");
        }

        return new WorkspaceReloadResult(added.Count, removed.Count, composed, programRunning);
    }

    // the environment host composes its session from the project file, and a class module's symbol is the project's: it composes it again, unless a program
    // runs in it.
    private async Task<(bool Composed, bool ProgramRunning)> ComposeHostAsync(string root, CancellationToken token)
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

    private TextDocumentIdentifier IdOf(string root, string relativePath) => new(ioPath.Combine(root, relativePath));
}
