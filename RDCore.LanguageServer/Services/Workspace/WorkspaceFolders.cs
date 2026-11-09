using RDCore.SDK.Workspace;

namespace RDCore.LanguageServer.Services.Workspace;

// the workspace is never widened past its folders: a path that climbs out of all of them is outside it. Case-insensitive on Windows only.
// RD-VBAL §2.2.3
public sealed class WorkspaceFolders
{
    // each root with a separator at its end, so that C:\Work\App holds C:\Work\App\X.bas and not C:\Work\Application\X.bas; the closest first.
    private readonly (string Root, string Prefix)[] _byDepth;

    public WorkspaceFolders(IEnumerable<string> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        Roots = [.. roots.Select(WorkspacePaths.Normalized).Distinct(WorkspacePaths.Comparer)];
        _byDepth = [.. Roots.Select(root => (root, Prefixed(root))).OrderByDescending(folder => folder.Item2.Length)];
    }

    public IReadOnlyList<string> Roots { get; }

    public string? RootOf(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var full = Prefixed(WorkspacePaths.Normalized(path));
        foreach (var (root, prefix) in _byDepth)
        {
            if (full.StartsWith(prefix, WorkspacePaths.Comparison))
            {
                return root;
            }
        }

        return null;
    }

    public bool Contains(string path) => RootOf(path) is not null;

    public static bool Holds(string root, string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Prefixed(WorkspacePaths.Normalized(path)).StartsWith(Prefixed(WorkspacePaths.Normalized(root)), WorkspacePaths.Comparison);
    }

    private static string Prefixed(string path) => path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;
}
