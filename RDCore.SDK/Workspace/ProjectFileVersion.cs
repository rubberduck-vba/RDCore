using RDCore.SDK.Server;

namespace RDCore.SDK.Workspace;

/// <summary>
/// The version a project file the running platform writes is stamped with (<see cref="ProjectFile.Version"/>).
/// </summary>
public static class ProjectFileVersion
{
    /// <summary>
    /// The version of the running platform, as <c>major.minor.build</c>.
    /// </summary>
    public static string Current => AppHost<RDCoreServerApp>.Info.Version!.ToString(3);

    /// <summary>
    /// The project file as the running platform writes it: stamped with its version (<see cref="Current"/>). A project file that is read, or copied, keeps
    /// the version that wrote it, so that a platform can refuse a project a later version wrote.
    /// </summary>
    /// <param name="project">The project file to write.</param>
    public static ProjectFile WithCurrentVersion(this ProjectFile project) => project.WithVersion(Current);
}
