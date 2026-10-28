namespace RDCore.SDK.Workspace;

/// <summary>
/// The folders of a workspace, as a client gives them (<strong>LSP 3.17</strong> <c>workspaceFolders</c>): they are the bounds of the workspace - a file a
/// folder holds is of the workspace, and a file no folder holds is nothing of it.
/// </summary>
/// <remarks>
/// <para>
/// A workspace folder is a project's (<strong>RD-VBAL §2.2.3</strong>): its <c>.rdproj</c>, or the one the platform makes up for a folder that has none - or it
/// only holds sources a project lists outside its own folder, a shared library in a sibling folder, and is no project (<see cref="WorkspaceFolderRoles"/> tells
/// which). The workspace is not widened beyond the folders it is given: a path that climbs out of every one of them (<c>..\</c>) is outside the workspace, and
/// is never followed.
/// </para>
/// <para>
/// A folder inside another, when the client gives both, is a folder of its own: <see cref="RootOf"/> is the one that holds a file most closely. Folders are
/// compared as full paths, without regard to case on Windows, where the file system has none.
/// </para>
/// </remarks>
public sealed class WorkspaceFolders
{
    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    // each root with a separator at its end, so that C:\Work\App holds C:\Work\App\X.bas and not C:\Work\Application\X.bas; the closest first.
    private readonly (string Root, string Prefix)[] _byDepth;

    /// <summary>
    /// The folders of a workspace.
    /// </summary>
    /// <param name="roots">The absolute paths of the folders, in the order the client gives them; a folder given twice is one folder.</param>
    public WorkspaceFolders(IEnumerable<string> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        Roots = [.. roots.Select(Normalized).Distinct(comparer)];
        _byDepth = [.. Roots.Select(root => (root, Prefixed(root))).OrderByDescending(folder => folder.Item2.Length)];
    }

    /// <summary>The folders, as full paths without a separator at their end, in the order the client gave them.</summary>
    public IReadOnlyList<string> Roots { get; }

    /// <summary>
    /// The folder whose project a file or folder is part of: the one that holds it most closely.
    /// </summary>
    /// <param name="path">An absolute path.</param>
    /// <returns>The folder, as it is in <see cref="Roots"/>; <see langword="null"/> when no folder holds it, and it is outside the workspace.</returns>
    public string? RootOf(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var full = Prefixed(Normalized(path));
        foreach (var (root, prefix) in _byDepth)
        {
            if (full.StartsWith(prefix, PathComparison))
            {
                return root;
            }
        }

        return null;
    }

    /// <summary>Whether a folder of the workspace holds a file or folder.</summary>
    /// <param name="path">An absolute path.</param>
    public bool Contains(string path) => RootOf(path) is not null;

    /// <summary>
    /// Whether a folder holds a file or folder, itself included.
    /// </summary>
    /// <param name="root">The absolute path of the folder.</param>
    /// <param name="path">An absolute path.</param>
    public static bool Holds(string root, string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Prefixed(Normalized(path)).StartsWith(Prefixed(Normalized(root)), PathComparison);
    }

    private static string Normalized(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static string Prefixed(string path) => path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;
}
