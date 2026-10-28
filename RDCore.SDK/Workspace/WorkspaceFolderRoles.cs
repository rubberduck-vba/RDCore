namespace RDCore.SDK.Workspace;

/// <summary>
/// What a folder of a workspace is for: a project's own folder, or a folder that holds sources some project lists.
/// </summary>
public enum WorkspaceFolderRole
{
    /// <summary>The folder of a project: its <c>.rdproj</c> is the project's, or the platform makes one up for it.</summary>
    Project = 0,

    /// <summary>
    /// A folder that may hold source files or other files, and is no project: a project lists files in it, and a <c>.rdproj</c> in it is ignored - a client
    /// can open and edit it without telling the language server.
    /// </summary>
    Source = 1,
}

/// <summary>
/// Tells which of the folders of a workspace are projects and which only hold sources a project lists (<strong>RD-VBAL §2.2.3</strong>).
/// </summary>
/// <remarks>
/// <para>
/// A client that serves a project whose sources lie outside its folder - a Visual Basic 6 <c>.vbp</c> that lists <c>..\Shared\Util.bas</c> - sends the
/// folders and nothing that tells them apart: first the project's, then the outside folders its files are in. The platform tells them by what it is told and what
/// it finds:
/// </para>
/// <list type="number">
/// <item>A folder that holds a project the client has imported (<c>rdcore.importProject</c>) is a project folder, and every other folder is a source folder.</item>
/// <item>
/// A client that imports nothing is read as follows: the first folder is a project, and a later folder is a source folder when a project that comes
/// before it lists files in it, and a project otherwise.
/// </item>
/// <item>An import that arrives corrects what the second rule guessed.</item>
/// </list>
/// <para>
/// The roles are worked out again each time they are asked, from the folders, the imports and the files the projects list: none of them is remembered, so that a
/// project that is changed or imported changes them.
/// </para>
/// </remarks>
public sealed class WorkspaceFolderRoles
{
    private readonly WorkspaceFolders _folders;
    private readonly HashSet<string> _imported;

    /// <summary>
    /// The roles of the folders of a workspace.
    /// </summary>
    /// <param name="folders">The folders of the workspace.</param>
    /// <param name="importedProjectFolders">
    /// The folders of the projects the client has imported: each is held by a folder of the workspace, which is a project folder. A folder that is held by none
    /// is no folder of this workspace, and is ignored.
    /// </param>
    public WorkspaceFolderRoles(WorkspaceFolders folders, IEnumerable<string> importedProjectFolders)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(importedProjectFolders);
        _folders = folders;

        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        _imported = new HashSet<string>(importedProjectFolders.Select(folders.RootOf).OfType<string>(), comparer);
    }

    /// <summary>
    /// The role of each folder of the workspace, in the order the client gave them.
    /// </summary>
    /// <param name="listedFilesOf">
    /// The absolute paths of the files a project lists, given the folder of the project. It is asked of the project folders that come before the folder being
    /// judged only, and only when the client has imported nothing.
    /// </param>
    public IReadOnlyList<(string Root, WorkspaceFolderRole Role)> Roles(Func<string, IEnumerable<string>> listedFilesOf)
    {
        ArgumentNullException.ThrowIfNull(listedFilesOf);

        if (_imported.Count > 0)
        {
            return [.. _folders.Roots.Select(root => (root, _imported.Contains(root) ? WorkspaceFolderRole.Project : WorkspaceFolderRole.Source))];
        }

        var roles = new List<(string Root, WorkspaceFolderRole Role)>(_folders.Roots.Count);
        var listed = new List<string>();
        foreach (var root in _folders.Roots)
        {
            // the first folder is a project; a later one is a source folder when a project before it lists a file in it.
            var isSource = roles.Count > 0 && listed.Any(path => IsUnder(root, path));
            roles.Add((root, isSource ? WorkspaceFolderRole.Source : WorkspaceFolderRole.Project));
            if (!isSource)
            {
                listed.AddRange(listedFilesOf(root));
            }
        }

        return roles;
    }

    private static bool IsUnder(string root, string path) => WorkspaceFolders.Holds(root, path);
}
