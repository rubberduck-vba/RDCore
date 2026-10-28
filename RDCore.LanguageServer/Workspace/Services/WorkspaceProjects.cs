using RDCore.SDK.Workspace;

namespace RDCore.LanguageServer.Workspace.Services;

/// <summary>
/// A project of the workspace: the folder it is in, and its project file.
/// </summary>
/// <param name="Root">The absolute path of the project's folder, as a workspace folder of the client.</param>
/// <param name="Project">The project file: the <c>.rdproj</c> of the folder, or the project the platform made up for it.</param>
internal sealed record WorkspaceProject(string Root, ProjectFile Project)
{
    /// <summary>
    /// The absolute paths of the files the project lists - its modules and its other files - which are the files of the project, wherever they are: a path that
    /// climbs out of the project's folder (<c>..\Shared\Util.bas</c>) is the file it names.
    /// </summary>
    public IReadOnlySet<string> ListedFiles
        => new HashSet<string>(
            Project.ProjectInfo.Modules.Select(module => module.RelativeUri).Concat(Project.ProjectInfo.OtherFiles.Select(file => file.RelativeUri))
                .Select(relative => Path.GetFullPath(Path.Combine(Root, relative))),
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
}

/// <summary>
/// The projects of the workspace (<strong>RD-VBAL §2.2.3</strong>): one for each folder the client gave that is a project's, and the bounds of the workspace.
/// </summary>
/// <remarks>
/// <para>
/// The first project is the primary one: the folder the language server was started on, which is the project the environment host runs. The others are
/// analyzed - diagnostics, navigation, completion - and not run (#252, S1b).
/// </para>
/// <para>
/// A folder is a project's, or only holds sources some project lists, as <see cref="WorkspaceFolderRoles"/> tells. A file is a module of every project that lists
/// it, each with a copy of its own as in Visual Basic 6; a file no project lists is none, wherever it is.
/// </para>
/// </remarks>
internal interface IWorkspaceProjects
{
    /// <summary>The folders the client gave; the bounds of the workspace. Only the primary one before <see cref="Initialize"/>.</summary>
    WorkspaceFolders Folders { get; }

    /// <summary>The projects, the primary one first.</summary>
    IReadOnlyList<WorkspaceProject> Projects { get; }

    /// <summary>
    /// Says which folders the client gave.
    /// </summary>
    /// <param name="primaryRoot">The folder the language server was started on.</param>
    /// <param name="folders">The workspace folders the client gave; the primary one is among them, whether the client lists it or not.</param>
    void Initialize(string primaryRoot, IEnumerable<string> folders);

    /// <summary>Makes a project of a folder.</summary>
    void Add(WorkspaceProject project);

    /// <summary>The projects that list a file, the one that was loaded first first.</summary>
    /// <param name="path">The absolute path of the file.</param>
    IReadOnlyList<WorkspaceProject> Listing(string path);

    /// <summary>
    /// Says that the client imported a project (<c>rdcore.importProject</c>): the folder that holds it is a project folder.
    /// </summary>
    /// <param name="projectFolder">The folder of the project that was imported.</param>
    void Imported(string projectFolder);

    /// <summary>
    /// The role of each folder the client gave, as it is told by the imports and by what the projects loaded so far list.
    /// </summary>
    IReadOnlyList<(string Root, WorkspaceFolderRole Role)> Roles();

    /// <summary>
    /// Takes note of a file a project lists that is outside every workspace folder, which the project does not have: it is refused, and said to the client.
    /// </summary>
    /// <param name="project">The root of the project that lists it.</param>
    /// <param name="relativeUri">The path as the project lists it.</param>
    void Refuse(string project, string relativeUri);

    /// <summary>The files that were refused, as <c>(project, path)</c>.</summary>
    IReadOnlyList<(string Project, string RelativeUri)> Refused { get; }
}

internal sealed class WorkspaceProjects(IProjectFileService primary) : IWorkspaceProjects
{
    private readonly object _sync = new();
    private readonly List<WorkspaceProject> _secondary = [];
    private readonly List<string> _imported = [];
    private readonly List<(string Project, string RelativeUri)> _refused = [];
    private volatile WorkspaceFolders _folders = new([]);
    private volatile string _primaryRoot = string.Empty;

    public WorkspaceFolders Folders => _folders;

    // the primary project is read live: the project file service holds it, and replaces it as it changes.
    public IReadOnlyList<WorkspaceProject> Projects
    {
        get
        {
            lock (_sync)
            {
                return primary.Project is { } project && _primaryRoot.Length > 0 ? [new WorkspaceProject(_primaryRoot, project), .. _secondary] : [.. _secondary];
            }
        }
    }

    public IReadOnlyList<(string Project, string RelativeUri)> Refused
    {
        get
        {
            lock (_sync)
            {
                return [.. _refused];
            }
        }
    }

    public void Initialize(string primaryRoot, IEnumerable<string> folders)
    {
        lock (_sync)
        {
            // the primary project is addressed by the root exactly as it arrived: whatever addresses a symbol derives it from the same string.
            _primaryRoot = primaryRoot;
            _folders = new WorkspaceFolders([primaryRoot, .. folders]);
            _secondary.Clear();
            _imported.Clear();
            _refused.Clear();
        }
    }

    public void Add(WorkspaceProject project)
    {
        lock (_sync)
        {
            _secondary.Add(project);
        }
    }

    public IReadOnlyList<WorkspaceProject> Listing(string path)
    {
        var full = Path.GetFullPath(path);
        return [.. Projects.Where(project => project.ListedFiles.Contains(full))];
    }

    public void Imported(string projectFolder)
    {
        lock (_sync)
        {
            _imported.Add(projectFolder);
        }
    }

    public IReadOnlyList<(string Root, WorkspaceFolderRole Role)> Roles()
    {
        string[] imported;
        lock (_sync)
        {
            imported = [.. _imported];
        }

        var projects = Projects.ToDictionary(project => Path.TrimEndingDirectorySeparator(Path.GetFullPath(project.Root)), project => project,
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        return new WorkspaceFolderRoles(_folders, imported).Roles(root => projects.TryGetValue(root, out var project) ? project.ListedFiles : []);
    }

    public void Refuse(string project, string relativeUri)
    {
        lock (_sync)
        {
            _refused.Add((project, relativeUri));
        }
    }
}
