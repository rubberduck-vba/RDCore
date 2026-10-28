namespace RDCore.SDK.Workspace;

/// <summary>
/// What a folder of a workspace is for: a project's own folder, or a folder that holds sources some project lists.
/// </summary>
public enum WorkspaceFolderRole
{
    /// <summary>The folder of a project: its <c>.rdproj</c> is the project's, or the platform makes one up for it.</summary>
    Project = 0,

    /// <summary>
    /// A folder that may hold source files or other files, and is no project: a project lists files in it, and a <c>.rdproj</c> in it is ignored when the project is an
    /// imported one - a client can open and edit it without telling the language server.
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
/// A later folder that an <em>imported</em> project lists files in is a source folder, even if it holds a <c>.rdproj</c> of its own, which is then ignored. A project is
/// an imported one when the import wrote its <c>.rdproj</c> beside its <c>.vbp</c> (<c>rdcore.importProject</c>); a <c>.vbp</c> that was never imported counts for nothing.
/// </item>
/// <item>A later folder that holds a <c>.rdproj</c> of its own is a project.</item>
/// <item>A later folder that holds no <c>.rdproj</c> is a source folder when a project that comes before it lists files in it, and a project when none does.</item>
/// </list>
/// <para>
/// The roles are worked out again each time they are asked, from the folders and the files the projects list: none of it is remembered. The language server asks when the
/// workspace is loaded: the projects it loaded stay as they were when a client imports one afterwards.
/// </para>
/// </remarks>
public sealed class WorkspaceFolderRoles
{
    private readonly WorkspaceFolders _folders;
    private readonly HashSet<string> _imported;
    private readonly HashSet<string> _withProject;

    /// <summary>
    /// The roles of the folders of a workspace.
    /// </summary>
    /// <param name="folders">The folders of the workspace.</param>
    /// <param name="importedProjectFolders">
    /// The folders of the workspace that hold a project that was imported: a <c>.vbp</c> with the <c>.rdproj</c> the import wrote beside it. A folder that is
    /// held by none is no folder of this workspace, and is ignored.
    /// </param>
    /// <param name="foldersWithProject">The folders of the workspace that hold a <c>.rdproj</c>.</param>
    public WorkspaceFolderRoles(WorkspaceFolders folders, IEnumerable<string> importedProjectFolders, IEnumerable<string> foldersWithProject)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(importedProjectFolders);
        ArgumentNullException.ThrowIfNull(foldersWithProject);
        _folders = folders;
        _imported = new HashSet<string>(importedProjectFolders.Select(folders.RootOf).OfType<string>(), WorkspacePaths.Comparer);
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
        var listedByImported = new List<string>();
        var listed = new List<string>();
        foreach (var root in _folders.Roots)
        {
            var isSource = roles.Count > 0
                && (listedByImported.Any(path => WorkspaceFolders.Holds(root, path)) || !_withProject.Contains(root) && listed.Any(path => WorkspaceFolders.Holds(root, path)));
            roles.Add((root, isSource ? WorkspaceFolderRole.Source : WorkspaceFolderRole.Project));
            if (!isSource)
            {
                var files = listedFilesOf(root).ToList();
                listed.AddRange(files);
                if (_imported.Contains(root))
                {
                    listedByImported.AddRange(files);
                }
            }
        }

        return roles;
    }
}
