using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Window;
using RDCore.LanguageServer.Debugging;
using RDCore.LanguageServer.Workspace.Services;

namespace RDCore.LanguageServer.Server.Handlers.Workspace;

/// <summary>
/// <strong>LSP 3.17</strong> <c>workspace/didChangeWorkspaceFolders</c>: the client added folders to the workspace or removed some, and the projects are loaded and
/// dropped as their folders come and go, as they are of the folders the client gave when it initialized (<see cref="IWorkspaceService.ChangeFoldersAsync"/>). A file
/// a project lists that no folder holds now is refused, and the client is told, as it is then.
/// </summary>
/// <remarks>
/// <para>
/// OmniSharp serves the notification itself, with its workspace folder manager, and that is what states <c>workspace.workspaceFolders.changeNotifications</c> in the
/// server's <c>initialize</c> result: a handler of the platform's own for the method would take the manager's place in OmniSharp's handlers, and the server would no
/// longer state it. The changes are read from what the manager says of each folder (<see cref="ILanguageServerWorkspaceFolderManager.Changed"/>), which it says as it
/// reads the notification (the folders added, then those removed) and does not wait for: each is made in the order it was said, one after the other.
/// </para>
/// <para>
/// The manager also says the folders of <c>initialize</c> once the server started: they are folders of the workspace already, and nothing is done.
/// </para>
/// </remarks>
internal interface IWorkspaceFolderChanges
{
    /// <summary>
    /// Reads the folders the client adds and removes from the workspace folder manager, until what is returned is disposed.
    /// </summary>
    /// <param name="folders">OmniSharp's workspace folder manager, which serves the notification.</param>
    /// <param name="server">The server, which tells its client.</param>
    IDisposable Attach(ILanguageServerWorkspaceFolderManager folders, ILanguageServerFacade server);

    /// <summary>
    /// Makes the workspace what its folders are now, and tells the client of each file a project lists that no folder holds now.
    /// </summary>
    /// <param name="added">The folders the client added.</param>
    /// <param name="removed">The folders the client removed.</param>
    /// <param name="server">The server, which tells its client.</param>
    /// <returns>A task that completes once the documents are what the projects list: the environment host and the symbols follow.</returns>
    Task ChangedAsync(IReadOnlyList<string> added, IReadOnlyList<string> removed, ILanguageServerFacade server);
}

internal sealed class WorkspaceFolderChanges(
    IWorkspaceReloadService reload,
    IWorkspaceService workspace,
    IWorkspaceProjects projects,
    ILogger<WorkspaceFolderChanges> logger) : IWorkspaceFolderChanges
{
    // the changes are made in the order the manager said them, each from what the one before made of the workspace.
    private readonly object _gate = new();
    private Task _last = Task.CompletedTask;

    public IDisposable Attach(ILanguageServerWorkspaceFolderManager folders, ILanguageServerFacade server)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(server);

        // a subscriber, not a handler: the manager stays the one that serves the notification, so the server keeps stating changeNotifications.
        return folders.Changed.Subscribe(new FolderObserver(change => InOrder(change, server)));
    }

    // said as the manager reads the notification, which it does not wait for: the change is queued, and made once those said before it are.
    private void InOrder(WorkspaceFolderChange change, ILanguageServerFacade server)
    {
        var folder = change.Folder.Uri.GetFileSystemPath();
        IReadOnlyList<string> added = change.Event == WorkspaceFolderEvent.Add ? [folder] : [];
        IReadOnlyList<string> removed = change.Event == WorkspaceFolderEvent.Remove ? [folder] : [];
        lock (_gate)
        {
            _last = _last.ContinueWith(_ => ChangedAsync(added, removed, server), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
        }
    }

    public async Task ChangedAsync(IReadOnlyList<string> added, IReadOnlyList<string> removed, ILanguageServerFacade server)
    {
        try
        {
            var refusedBefore = projects.Refused;
            if (await reload.BeginChangeFoldersAsync(token => workspace.ChangeFoldersAsync(added, removed, token), CancellationToken.None) is not { } step)
            {
                return;
            }

            _ = WorkspaceFileOperationFilters.ReportCompletedAsync(step.Completed, server, logger);
            TellRefused(server, projects.Refused.Except(refusedBefore));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "❌ The workspace folders changed, and the workspace could not be made what they hold.");
        }
    }

    /// <summary>
    /// Tells the client of each file a project lists that no workspace folder holds, which the project does not have (<see cref="IWorkspaceProjects.Refused"/>).
    /// </summary>
    /// <param name="server">The server, which tells its client.</param>
    /// <param name="refused">The files, as <c>(project, path)</c>.</param>
    public static void TellRefused(ILanguageServerFacade server, IEnumerable<(string Project, string RelativeUri)> refused)
    {
        foreach (var (project, relativeUri) in refused)
        {
            server.Window.ShowMessage(new ShowMessageParams
            {
                Type = MessageType.Warning,
                Message = string.Format(DebuggerMessages.FileOutsideTheWorkspaceFolders, relativeUri, project),
            });
        }
    }

    // what the manager says, as it says it; it says nothing when it ends.
    private sealed class FolderObserver(Action<WorkspaceFolderChange> next) : IObserver<WorkspaceFolderChange>
    {
        public void OnNext(WorkspaceFolderChange value) => next(value);

        public void OnError(Exception error)
        {
        }

        public void OnCompleted()
        {
        }
    }
}
