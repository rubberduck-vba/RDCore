using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Window;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Workspace;

namespace RDCore.LanguageServer.Server.Handlers.Workspace;

/// <summary>
/// Files of the workspace that changed outside the editor, as a client says (<c>workspace/didChangeWatchedFiles</c>) or as the server sees when it watches them
/// for a client that does not (<see cref="WorkspaceFileSystemWatcher"/>): the workspace is made what they are, and the client is told.
/// </summary>
/// <remarks>
/// <para>
/// The workspace's own <c>.rdproj</c>, created or changed, is read again (<see cref="IWorkspaceReloadService.ReloadAsync"/>); a deleted one, and another folder's,
/// are left alone. A file the project lists that was changed, created or deleted is read again (<see cref="IWorkspaceReloadService.ReloadFilesAsync"/>); one it
/// does not list is nothing of the workspace.
/// </para>
/// <para>
/// What the server wrote itself, coming back, is what the workspace is, and nothing is done. A client that pulls its diagnostics is asked to pull them again
/// once something was (<see cref="WorkspaceChangeReport"/>).
/// </para>
/// </remarks>
internal interface IWatchedFileChanges
{
    /// <summary>Makes the workspace what the files that changed are, and tells the client.</summary>
    /// <param name="changes">The files, and how each changed.</param>
    /// <param name="server">The server, which tells its client.</param>
    Task ChangedAsync(IReadOnlyList<FileEvent> changes, ILanguageServerFacade server);
}

internal sealed class WatchedFileChanges(
    IWorkspaceReloadService reload,
    IWorkspaceDocumentService documents,
    ILogger<WatchedFileChanges> logger) : IWatchedFileChanges
{
    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public async Task ChangedAsync(IReadOnlyList<FileEvent> changes, ILanguageServerFacade server)
    {
        if (changes.Any(IsTheProjectFileWritten))
        {
            await RunAsync(() => reload.ReloadAsync(CancellationToken.None), server, projectFile: true);
        }

        var files = changes.Where(change => !IsTheProjectFile(change)).Select(change => change.Uri.ToUri()).ToList();
        if (files.Count > 0)
        {
            await RunAsync(() => reload.ReloadFilesAsync(files, CancellationToken.None), server, projectFile: false);
        }
    }

    private async Task RunAsync(Func<Task<WorkspaceReloadResult?>> reloading, ILanguageServerFacade server, bool projectFile)
    {
        try
        {
            if (await reloading() is { } result)
            {
                WorkspaceChangeReport.Send(server, result);
            }
        }
        catch (InvalidDataException exception) when (projectFile)
        {
            logger.LogWarning(exception, "⚠️ The project file changed, and could not be read; the workspace is as it was.");
            server.Window.ShowMessage(new ShowMessageParams { Type = MessageType.Warning, Message = string.Format(WorkspaceMessages.ProjectFileNotReloaded, exception.Message) });
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "❌ Files of the workspace changed, and the workspace could not be made what they are.");
        }
    }

    private bool IsTheProjectFile(FileEvent change)
        => string.Equals(Path.GetFullPath(change.Uri.GetFileSystemPath()), Path.GetFullPath(Path.Combine(documents.WorkspaceRoot, ProjectFile.FileName)), PathComparison);

    // the workspace's own project file, created or changed: a deleted one leaves the workspace as it was.
    private bool IsTheProjectFileWritten(FileEvent change) => change.Type is FileChangeType.Created or FileChangeType.Changed && IsTheProjectFile(change);
}
