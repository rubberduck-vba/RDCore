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

namespace RDCore.LanguageServer.Workspace.Services;

/// <summary>
/// Makes the workspace what its project file lists now, once a client said the <c>.rdproj</c> changed: the files it lists that were not loaded are loaded and
/// parsed, the ones it no longer lists are taken out, and the whole workspace is defined again in the environment host, in its three phases.
/// </summary>
/// <remarks>
/// The project file is read when the server initializes (<see cref="IWorkspaceService.LoadAsync"/>); a client that rewrites it while the server runs says so with
/// <c>workspace/didChangeWatchedFiles</c>, and this is what is done about it. The client owns the file system: the server is told, it does not watch.
/// </remarks>
internal interface IWorkspaceReloadService
{
    /// <summary>
    /// Reads the project file again, and makes the workspace what it lists.
    /// </summary>
    /// <param name="token">A token that cancels the reload.</param>
    /// <returns>What the reload changed, or <see langword="null"/> when the server is not running a workspace it could reload.</returns>
    /// <exception cref="InvalidDataException">The project file is not valid; the workspace is left as it was.</exception>
    Task<WorkspaceReloadResult?> ReloadAsync(CancellationToken token);
}

/// <summary>
/// What reloading the project file changed.
/// </summary>
/// <param name="Added">The files the project lists now that it did not.</param>
/// <param name="Removed">The files it no longer lists.</param>
/// <param name="IsHostComposed">Whether the environment host composed its runtime session again from the project file (<see cref="ProjectReload"/>).</param>
/// <param name="IsProgramRunning">Whether the host kept its session, a program running or waiting in it.</param>
internal sealed record class WorkspaceReloadResult(int Added, int Removed, bool IsHostComposed, bool IsProgramRunning);

internal sealed class WorkspaceReloadService(
    IServerStateProvider serverStateProvider,
    IProjectFileService projectFiles,
    IWorkspaceDocumentService documents,
    IDocumentStateProvider documentStates,
    IParsingClientService parsing,
    ISymbolSyncService symbols,
    IPlatformOrchestrationService orchestration,
    System.IO.Abstractions.IPath ioPath,
    ILogger<WorkspaceReloadService> logger) : IWorkspaceReloadService
{
    // one reload at a time: a client that writes the project file twice in a row is told about it twice, and the second reload reads what the second write
    // left.
    private readonly SemaphoreSlim _reloading = new(1, 1);

    public async Task<WorkspaceReloadResult?> ReloadAsync(CancellationToken token)
    {
        // running, whatever the trace a client asked for: a server that is traced nothing is RunningTraceless, and still running.
        if (serverStateProvider.State is not RunningServerState || projectFiles.Project?.ProjectInfo is null)
        {
            logger.LogInformation("The project file changed before a workspace was loaded; it is read when the workspace is.");
            return null;
        }

        await _reloading.WaitAsync(token);
        try
        {
            return await ReloadProjectAsync(token);
        }
        finally
        {
            _reloading.Release();
        }
    }

    private async Task<WorkspaceReloadResult> ReloadProjectAsync(CancellationToken token)
    {
        var root = projectFiles.Project.Uri;
        var before = FilesOf(projectFiles.Project);

        // a project file that is not valid leaves the workspace as it was: the loader throws, and nothing was changed yet.
        await projectFiles.LoadAsync(root);
        var after = FilesOf(projectFiles.Project);

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
