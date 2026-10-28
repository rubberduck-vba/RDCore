namespace RDCore.SDK.Workspace;

/// <summary>
/// How the paths of a workspace are compared and read: the one place that says so, for the folders of a workspace, the projects that list files, and the documents
/// that are loaded.
/// </summary>
public static class WorkspacePaths
{
    /// <summary>How two paths are compared: without regard to case on Windows, where the file system has none.</summary>
    public static StringComparison Comparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>The comparer of paths, as <see cref="Comparison"/> compares them.</summary>
    public static StringComparer Comparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>A path as a full path with no separator at its end.</summary>
    /// <param name="path">A path.</param>
    public static string Normalized(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    /// <summary>Whether two paths are the same file or folder.</summary>
    /// <param name="left">A path.</param>
    /// <param name="right">Another path.</param>
    public static bool Same(string left, string right) => string.Equals(Normalized(left), Normalized(right), Comparison);

    /// <summary>
    /// Whether a path climbs out of the folder it is relative to: it has a <c>..</c> in it, written with either separator (<c>..\Shared\Util.bas</c>).
    /// </summary>
    /// <param name="relativePath">A path as a project lists it.</param>
    public static bool Climbs(string relativePath) => relativePath.Split('/', '\\').Contains("..");
}
