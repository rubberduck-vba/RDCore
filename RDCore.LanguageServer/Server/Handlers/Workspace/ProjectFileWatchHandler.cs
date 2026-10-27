using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;
using RDCore.SDK.Workspace;

namespace RDCore.LanguageServer.Server.Handlers.Workspace;

/// <summary>
/// <c>workspace/didChangeWatchedFiles</c>: the client says files of the workspace changed outside the editor - its <c>.rdproj</c>, or a source file - and the
/// workspace is made what they are (<see cref="IWatchedFileChanges"/>).
/// </summary>
/// <remarks>
/// <para>
/// The client owns the file system: a client that says it watches files is told nothing of what the server could watch, and the server watches nothing. A
/// client that registers capabilities dynamically is asked to watch the workspace's <c>.rdproj</c> and the source files of every language the platform serves;
/// one that does not sends the notification all the same, and is served the same way.
/// </para>
/// <para>
/// The notification is answered at once, and the workspace is made what the files are as long as that takes.
/// </para>
/// </remarks>
internal sealed class ProjectFileWatchHandler(
    IWatchedFileChanges changes,
    ILanguageServerFacade server) : DidChangeWatchedFilesHandlerBase
{
    public override Task<Unit> Handle(DidChangeWatchedFilesParams request, CancellationToken cancellationToken)
    {
        _ = changes.ChangedAsync([.. request.Changes], server);
        return Unit.Task;
    }

    protected override DidChangeWatchedFilesRegistrationOptions CreateRegistrationOptions(DidChangeWatchedFilesCapability capability, ClientCapabilities clientCapabilities)
        => new()
        {
            Watchers = new Container<OmniSharp.Extensions.LanguageServer.Protocol.Models.FileSystemWatcher>(
                new OmniSharp.Extensions.LanguageServer.Protocol.Models.FileSystemWatcher
                {
                    GlobPattern = new GlobPattern($"**/{ProjectFile.FileName}"),
                    Kind = WatchKind.Create | WatchKind.Change,
                },
                new OmniSharp.Extensions.LanguageServer.Protocol.Models.FileSystemWatcher
                {
                    GlobPattern = new GlobPattern($"**/*.{{{string.Join(',', WorkspaceFileSystemWatcher.SourceExtensions)}}}"),
                    Kind = WatchKind.Create | WatchKind.Change | WatchKind.Delete,
                }),
        };
}
