using MediatR;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Window;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Workspace;

namespace RDCore.LanguageServer.Server.Handlers.Workspace;

/// <summary>
/// What the file operations a client tells the server about are filtered by: the source files of every language the platform serves, and folders.
/// </summary>
/// <remarks>
/// A folder is what a client renames or deletes with the source files it holds, so every folder is of interest; the server keeps of it what is the project's
/// (<see cref="IWorkspaceFileOperationService"/>).
/// </remarks>
internal static class WorkspaceFileOperationFilters
{
    public static Container<FileOperationFilter> All { get; } = new(SupportedLanguages.All
        .SelectMany(language => language.FileTypes).Distinct()
        .Select(fileType => Filter($"**/{fileType}", FileOperationPatternKind.File))
        .Append(Filter("**/*", FileOperationPatternKind.Folder)));

    /// <summary>
    /// <c>workspace.fileOperations.didRename</c>, as the server's <c>initialize</c> result states it: <see cref="DidRenameWorkspaceFilesHandler"/> is not an
    /// OmniSharp handler, which OmniSharp would state for it.
    /// </summary>
    public static DidRenameFileRegistrationOptions.StaticOptions DidRename { get; } = new() { Filters = All };

    private static FileOperationFilter Filter(string glob, FileOperationPatternKind kind) => new()
    {
        Scheme = "file",
        Pattern = new FileOperationPattern { Glob = glob, Matches = kind, Options = new FileOperationPatternOptions { IgnoreCase = true } },
    };

    /// <summary>
    /// Tells the client what came of its file operations, which are done in the background - the notification is answered at once - in the order it sent them.
    /// </summary>
    public static void Report(Task<WorkspaceFileOperationResult?> operation, ILanguageServerFacade server, ILogger logger)
        => _ = ReportAsync(operation, server, logger);

    private static async Task ReportAsync(Task<WorkspaceFileOperationResult?> operation, ILanguageServerFacade server, ILogger logger)
    {
        try
        {
            // the operation was started by the handler that received the notification, in the service that puts the client's operations in order.
#pragma warning disable VSTHRD003
            if (await operation is not { } result)
#pragma warning restore VSTHRD003
            {
                return;
            }

            foreach (var (relativeUri, moduleName) in result.NotAdded)
            {
                server.Window.ShowMessage(new ShowMessageParams
                {
                    Type = MessageType.Warning,
                    Message = string.Format(WorkspaceMessages.FileNotAddedModuleNameTaken, relativeUri, moduleName),
                });
            }

            WorkspaceChangeReport.Send(server, result.Reload);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "❌ The client's file operations could not be made the project's.");
            server.Window.ShowMessage(new ShowMessageParams { Type = MessageType.Error, Message = string.Format(WorkspaceMessages.ProjectFileNotWritten, exception.Message) });
        }
    }
}

/// <summary>
/// <c>workspace/didCreateFiles</c>: the client created files or folders, and the source files are modules of the project.
/// </summary>
internal sealed class DidCreateWorkspaceFilesHandler(
    IWorkspaceFileOperationService files,
    ILanguageServerFacade server,
    ILogger<DidCreateWorkspaceFilesHandler> logger) : DidCreateFileHandlerBase
{
    public override Task<Unit> Handle(DidCreateFileParams request, CancellationToken cancellationToken)
    {
        var uris = request.Files.Select(file => file.Uri).ToList();
        WorkspaceFileOperationFilters.Report(files.CreatedAsync(uris, CancellationToken.None), server, logger);
        return Unit.Task;
    }

    protected override DidCreateFileRegistrationOptions CreateRegistrationOptions(FileOperationsWorkspaceClientCapabilities capability, ClientCapabilities clientCapabilities)
        => new() { Filters = WorkspaceFileOperationFilters.All };
}

/// <summary>
/// <c>workspace/didRenameFiles</c>: the client renamed or moved files or folders, and the project lists them where they are now.
/// </summary>
/// <remarks>
/// The notification is read as <see cref="DidRenameWorkspaceFilesParams"/>, which OmniSharp does not register: the server says it handles it in its
/// <c>initialize</c> result (<see cref="WorkspaceFileOperationFilters.DidRename"/>).
/// </remarks>
internal sealed class DidRenameWorkspaceFilesHandler(
    IWorkspaceFileOperationService files,
    ILanguageServerFacade server,
    ILogger<DidRenameWorkspaceFilesHandler> logger) : IJsonRpcNotificationHandler<DidRenameWorkspaceFilesParams>
{
    public Task<Unit> Handle(DidRenameWorkspaceFilesParams request, CancellationToken cancellationToken)
    {
        // read as a document URI is: a client escapes a drive letter's colon (file:///c%3A/...).
        var renames = request.Files.Select(file => (DocumentUri.From(file.OldUri).ToUri(), DocumentUri.From(file.NewUri).ToUri())).ToList();
        WorkspaceFileOperationFilters.Report(files.RenamedAsync(renames, CancellationToken.None), server, logger);
        return Unit.Task;
    }
}

/// <summary>
/// <c>workspace/didDeleteFiles</c>: the client deleted files or folders, and the project no longer lists them.
/// </summary>
internal sealed class DidDeleteWorkspaceFilesHandler(
    IWorkspaceFileOperationService files,
    ILanguageServerFacade server,
    ILogger<DidDeleteWorkspaceFilesHandler> logger) : DidDeleteFileHandlerBase
{
    public override Task<Unit> Handle(DidDeleteFileParams request, CancellationToken cancellationToken)
    {
        var uris = request.Files.Select(file => file.Uri).ToList();
        WorkspaceFileOperationFilters.Report(files.DeletedAsync(uris, CancellationToken.None), server, logger);
        return Unit.Task;
    }

    protected override DidDeleteFileRegistrationOptions CreateRegistrationOptions(FileOperationsWorkspaceClientCapabilities capability, ClientCapabilities clientCapabilities)
        => new() { Filters = WorkspaceFileOperationFilters.All };
}
