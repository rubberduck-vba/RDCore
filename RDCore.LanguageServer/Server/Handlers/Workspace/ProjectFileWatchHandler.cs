using MediatR;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Window;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;
using RDCore.LanguageServer.Diagnostics;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Workspace;

namespace RDCore.LanguageServer.Server.Handlers.Workspace;

/// <summary>
/// <c>workspace/didChangeWatchedFiles</c>: the client created or changed the workspace's <c>.rdproj</c>, and the workspace is made what it lists now
/// (<see cref="IWorkspaceReloadService"/>).
/// </summary>
/// <remarks>
/// <para>
/// The client owns the file system: the server is told that the project file changed, and watches nothing itself. A client that registers capabilities
/// dynamically is asked to watch <c>**/.rdproj</c>; one that does not sends the notification all the same, and is served the same way.
/// </para>
/// <para>
/// The notification is answered at once, and the reload takes as long as defining the workspace takes. A client that pulls its diagnostics is then asked to
/// pull them again (<c>workspace/diagnostic/refresh</c>). A deleted project file is left alone: the workspace stays as it was.
/// </para>
/// </remarks>
internal sealed class ProjectFileWatchHandler(
    IWorkspaceReloadService reload,
    IWorkspaceDocumentService documents,
    ILanguageServerFacade server,
    ILogger<ProjectFileWatchHandler> logger) : DidChangeWatchedFilesHandlerBase
{
    public override Task<Unit> Handle(DidChangeWatchedFilesParams request, CancellationToken cancellationToken)
    {
        if (request.Changes.Any(IsTheProjectFileWritten))
        {
            _ = ReloadAsync();
        }

        return Unit.Task;
    }

    protected override DidChangeWatchedFilesRegistrationOptions CreateRegistrationOptions(DidChangeWatchedFilesCapability capability, ClientCapabilities clientCapabilities)
        => new()
        {
            Watchers = new Container<OmniSharp.Extensions.LanguageServer.Protocol.Models.FileSystemWatcher>(new OmniSharp.Extensions.LanguageServer.Protocol.Models.FileSystemWatcher
            {
                GlobPattern = new GlobPattern($"**/{ProjectFile.FileName}"),
                Kind = WatchKind.Create | WatchKind.Change,
            }),
        };

    // the workspace's own project file, created or changed: what another folder holds is not this workspace's, and a deleted one leaves the workspace as it was.
    private bool IsTheProjectFileWritten(FileEvent change)
        => change.Type is FileChangeType.Created or FileChangeType.Changed
            && string.Equals(Path.GetFullPath(change.Uri.GetFileSystemPath()), Path.GetFullPath(Path.Combine(documents.WorkspaceRoot, ProjectFile.FileName)),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private async Task ReloadAsync()
    {
        try
        {
            // the server's own write of the project file, which a client that watches it says changed, lists what the workspace is: nothing is done.
            if (await reload.ReloadAsync(CancellationToken.None) is { } result)
            {
                WorkspaceChangeReport.Send(server, result);
            }
        }
        catch (InvalidDataException exception)
        {
            logger.LogWarning(exception, "⚠️ The project file changed, and could not be read; the workspace is as it was.");
            server.Window.ShowMessage(new ShowMessageParams { Type = MessageType.Warning, Message = string.Format(WorkspaceMessages.ProjectFileNotReloaded, exception.Message) });
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "❌ The project file changed, and the workspace could not be loaded again.");
        }
    }
}
