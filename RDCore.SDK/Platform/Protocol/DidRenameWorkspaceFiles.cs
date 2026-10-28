using MediatR;
using Newtonsoft.Json;
using OmniSharp.Extensions.JsonRpc;

namespace RDCore.SDK.Platform.Protocol;

/// <summary>
/// <c>workspace/didRenameFiles</c> (<strong>LSP 3.17</strong> §Workspace, DidRenameFiles Notification): a client renamed or moved files or folders.
/// </summary>
/// <remarks>
/// OmniSharp 0.19.9 reads the notification as <c>DidRenameFileParams</c>, whose <c>FileRename</c> has only a <c>uri</c>: the <c>oldUri</c> and <c>newUri</c> a
/// client sends are not read, and every rename is one from nowhere to nowhere. This is the same notification, read as the protocol states it. A server that handles
/// it says so in its <c>initialize</c> result itself (<c>workspace.fileOperations.didRename</c>), since no OmniSharp handler registers it. The notification is
/// serial: OmniSharp reads a serial message after every message before it is done, and the ones after it once it is. A client sends <c>textDocument/didClose</c> for
/// the old path and <c>didOpen</c> for the new one before it says the files were renamed, and a server that reads the rename beside them sees a document open at a path
/// before it has moved there. The group of parallel messages before a serial one is done before it is read, so the rename is made after the close and before the
/// notifications that follow it.
/// </remarks>
[Serial, Method("workspace/didRenameFiles", Direction.ClientToServer)]
public sealed record class DidRenameWorkspaceFilesParams : IRequest
{
    /// <summary>The files and folders that were renamed, in the order the client renamed them.</summary>
    [JsonProperty("files")]
    public WorkspaceFileRename[] Files { get; init; } = [];
}

/// <summary>
/// A file or folder a client renamed (<strong>LSP 3.17</strong> <c>FileRename</c>).
/// </summary>
public sealed record class WorkspaceFileRename
{
    /// <summary>A <c>file://</c> URI of where the file or folder was.</summary>
    [JsonProperty("oldUri")]
    public string OldUri { get; init; } = string.Empty;

    /// <summary>A <c>file://</c> URI of where the file or folder is now.</summary>
    [JsonProperty("newUri")]
    public string NewUri { get; init; } = string.Empty;
}
