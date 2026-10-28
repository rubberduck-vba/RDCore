using NSubstitute;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Workspace;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// The registry of a workspace of one project, for a service that asks the workspace's projects what it composes: the primary project, which the service has from the
/// document service, and no other.
/// </summary>
internal static class OneProject
{
    public static IWorkspaceProjects Registry()
    {
        var projects = Substitute.For<IWorkspaceProjects>();
        projects.Projects.Returns([]);
        projects.Folders.Returns(new WorkspaceFolders([]));
        return projects;
    }
}
