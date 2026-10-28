using NSubstitute;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// A substitute workspace that holds documents as the workspace does: listed as its modules, and found by their addresses.
/// </summary>
internal static class WorkspaceDocumentsStub
{
    /// <summary>The documents the workspace holds: each a module, found by its address; the files among them listed.</summary>
    public static void Holds(this IWorkspaceDocumentService documents, params WorkspaceDocument[] held)
    {
        documents.GetAllDocuments().Returns([.. held.Where(document => document.IsFile)]);
        foreach (var document in held)
        {
            var uri = document.Id.Uri.ToUri();
            documents.TryGetDocument(uri, out Arg.Any<WorkspaceDocument>()).Returns(call => { call[1] = document; return true; });
            documents.TryGetModule(uri, out Arg.Any<WorkspaceDocument>()).Returns(call => { call[1] = document; return true; });
        }
    }
}
