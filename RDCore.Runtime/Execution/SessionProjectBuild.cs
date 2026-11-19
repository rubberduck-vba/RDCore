using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Workspace;
using System.Runtime.CompilerServices;

namespace RDCore.Runtime.Execution;

// the build section of the project a session was composed with (RDCoreProject.Build), which says the procedure a run of the project starts with and whether
// the project is run at all; none for a project file that has none, or a session composed without a project.
public static class SessionProjectBuild
{
    private static readonly ConditionalWeakTable<IRuntimeSession, ProjectBuild> Builds = new();

    public static void Set(IRuntimeSession session, ProjectBuild build) => Builds.AddOrUpdate(session, build);

    public static ProjectBuild? Of(IRuntimeSession session) => Builds.TryGetValue(session, out var build) ? build : null;
}
