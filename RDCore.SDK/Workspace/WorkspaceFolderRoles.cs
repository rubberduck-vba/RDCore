namespace RDCore.SDK.Workspace;

/// <summary>
/// What a folder of a workspace is for: a project's own folder, or a folder that holds sources some project lists.
/// </summary>
public enum WorkspaceFolderRole
{
    /// <summary>The folder of a project: its <c>.rdproj</c> is the project's, or the platform makes one up for it.</summary>
    Project = 0,

    /// <summary>
    /// A folder that may hold source files or other files, has no <c>.rdproj</c>, and is no project: a project lists files in it - a client can open and edit it without
    /// telling the language server.
    /// </summary>
    Source = 1,
}

/// <summary>
/// Tells which of the folders of a workspace are projects and which only hold sources a project lists (<strong>RD-VBAL §2.2.3</strong>).
/// </summary>
/// <remarks>
/// <para>
/// A client that serves a project whose sources lie outside its folder - a Visual Basic 6 <c>.vbp</c> that lists <c>..\Shared\Util.bas</c> - sends the
/// folders and nothing that tells them apart: first the project's, then the outside folders its files are in. The folders are judged in the order they are given:
/// </para>
/// <list type="number">
/// <item>The first folder is a project always: it is the folder the server runs.</item>
/// <item>
/// A later folder that holds a <c>.rdproj</c> of its own is a project, whether it was imported (<see cref="RDCoreProject.SourceProject"/>) or is a native one, and
/// even when another project lists files in it: a file two projects list is a module of each.
/// </item>
/// <item>A later folder that holds no <c>.rdproj</c> is a source folder when a project that comes before it lists files in it, and a project when none does.</item>
/// </list>
/// <para>
/// The roles are worked out again each time they are asked, from the folders and the files the projects list: none of it is remembered. The language server asks when the
/// workspace is loaded.
/// </para>
/// </remarks>
public sealed class WorkspaceFolderRoles
{
    private readonly WorkspaceFolders _folders;
    private readonly HashSet<string> _withProject;

    /// <summary>
    /// The roles of the folders of a workspace.
    /// </summary>
    /// <param name="folders">The folders of the workspace.</param>
    /// <param name="foldersWithProject">The folders of the workspace that hold a <c>.rdproj</c>. A folder that is held by none is no folder of this workspace, and is ignored.</param>
    public WorkspaceFolderRoles(WorkspaceFolders folders, IEnumerable<string> foldersWithProject)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(foldersWithProject);
        _folders = folders;
        _withProject = new HashSet<string>(foldersWithProject.Select(folders.RootOf).OfType<string>(), WorkspacePaths.Comparer);
    }

    /// <summary>
    /// The role of each folder of the workspace, in the order the client gave them.
    /// </summary>
    /// <param name="listedFilesOf">
    /// The absolute paths of the files a project lists, given the folder of the project. It is asked of the project folders that come before the folder being judged only.
    /// </param>
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
