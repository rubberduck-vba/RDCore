using RDCore.SDK.Workspace;

namespace RDCore.LanguageServer.Workspace.Services;

internal static class WorkspaceDocumentServiceExtensions
{
    /// <summary>
    /// The documents that are modules of one project, as <see cref="IWorkspaceDocumentService.GetAllDocuments"/> has them: the files the project lists, and what a
    /// client opened that is in the project's own folder. A file that several projects list is in each.
    /// </summary>
    /// <param name="documents">The documents of the workspace.</param>
    /// <param name="projectRoot">The folder of the project; none when no workspace is loaded, and then every document is the one project's.</param>
    public static IEnumerable<WorkspaceDocument> GetProjectDocuments(this IWorkspaceDocumentService documents, string projectRoot)
        => string.IsNullOrEmpty(projectRoot)
            ? documents.GetAllDocuments()
            : documents.GetAllDocuments().Where(document => document.Projects.Any(root => WorkspacePaths.Same(root, projectRoot)));
}
