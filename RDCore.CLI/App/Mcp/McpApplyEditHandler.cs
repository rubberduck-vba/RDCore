using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;
using RDCore.SDK.Client;

namespace RDCore.CLI.App.Mcp;

// workspace/applyEdit under rdc mcp: an edit the language server makes is written to the files, which rdc keeps closed, and the server is told which files
// changed before it is answered, so that it reads them again before it answers what it is asked next.
internal sealed class McpApplyEditHandler(WorkspaceEditFiles files, ILanguageClientFacade client) : IApplyWorkspaceEditHandler
{
    public async Task<ApplyWorkspaceEditResponse> Handle(ApplyWorkspaceEditParams request, CancellationToken cancellationToken)
    {
        // rdc opens no document, so it knows no version: a change that names one is refused.
        var result = await files.ApplyAsync(request.Edit, _ => null, cancellationToken);
        if (result.Written.Count > 0)
        {
            client.Workspace.DidChangeWatchedFiles(new DidChangeWatchedFilesParams
            {
                Changes = new Container<FileEvent>(result.Written.Select(path => new FileEvent { Uri = DocumentUri.FromFileSystemPath(path), Type = FileChangeType.Changed })),
            });
        }

        return result.ToResponse();
    }
}
