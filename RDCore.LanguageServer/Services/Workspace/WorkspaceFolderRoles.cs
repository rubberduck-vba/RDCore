using RDCore.SDK.Workspace;

namespace RDCore.LanguageServer.Services.Workspace;

public enum WorkspaceFolderRole
{
    Project = 0,

    Source = 1,
}

// a client sends folders it does not tell apart (a .vbp's ..\Shared): the first is always a project, a later one with its own .rdproj is a project, and one
// without is a source folder when an earlier project lists files in it. Worked out on every ask, never remembered.
// RD-VBAL §2.2.3
public sealed class WorkspaceFolderRoles
{
    private readonly WorkspaceFolders _folders;
    private readonly HashSet<string> _withProject;

    public WorkspaceFolderRoles(WorkspaceFolders folders, IEnumerable<string> foldersWithProject)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(foldersWithProject);
        _folders = folders;
        _withProject = new HashSet<string>(foldersWithProject.Select(folders.RootOf).OfType<string>(), WorkspacePaths.Comparer);
    }

    public IReadOnlyList<(string Root, WorkspaceFolderRole Role)> Roles(Func<string, IEnumerable<string>> listedFilesOf)
    {
        ArgumentNullException.ThrowIfNull(listedFilesOf);

        var roles = new List<(string Root, WorkspaceFolderRole Role)>(_folders.Roots.Count);
        var listed = new List<string>();
        foreach (var root in _folders.Roots)
        {
            var isSource = roles.Count > 0 && !_withProject.Contains(root) && listed.Any(path => WorkspaceFolders.Holds(root, path));
            roles.Add((root, isSource ? WorkspaceFolderRole.Source : WorkspaceFolderRole.Project));
            if (!isSource)
            {
                listed.AddRange(listedFilesOf(root));
            }
        }

        return roles;
    }
}
