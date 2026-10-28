using RDCore.SDK.Workspace;

namespace RDCore.LanguageServer.Workspace.Services;

/// <summary>
/// A project of the workspace: the folder it is in, and its project file.
/// </summary>
/// <param name="root">The absolute path of the project's folder, as a workspace folder of the client.</param>
/// <param name="project">The project file: the <c>.rdproj</c> of the folder, or the project the platform made up for it.</param>
internal sealed class WorkspaceProject(string root, ProjectFile project)
{
    private IReadOnlySet<string>? _listedFiles;

    /// <summary>The absolute path of the project's folder, as a workspace folder of the client.</summary>
    public string Root => root;

    /// <summary>The project file: the <c>.rdproj</c> of the folder, or the project the platform made up for it.</summary>
    public ProjectFile Project => project;

    /// <summary>
    /// The absolute paths of the files the project lists - its modules and its other files - which are the files of the project, wherever they are: a path that
    /// climbs out of the project's folder (<c>..\Shared\Util.bas</c>) is the file it names. Worked out once for the project file the project has.
    /// </summary>
    public IReadOnlySet<string> ListedFiles
        => _listedFiles ??= new HashSet<string>(
            project.ProjectInfo.Modules.Select(module => module.RelativeUri).Concat(project.ProjectInfo.OtherFiles.Select(file => file.RelativeUri))
                .Select(relative => Path.GetFullPath(Path.Combine(root, relative))),
            WorkspacePaths.Comparer);
}

/// <summary>
/// The projects of the workspace (<strong>RD-VBAL §2.2.3</strong>): one for each folder the client gave that is a project's, and the bounds of the workspace.
/// </summary>
/// <remarks>
/// <para>
/// The first project is the primary one: the folder the language server was started on, which is the project the environment host runs. The others are
/// analyzed - diagnostics, navigation, completion - and not run.
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
    /// <param name="importedProjectFolders">The folders that hold a Visual Basic 6 project (<c>.vbp</c>) that was imported: they are the project folders (<see cref="WorkspaceFolderRoles"/>).</param>
    void Initialize(string primaryRoot, IEnumerable<string> folders, IEnumerable<string> importedProjectFolders);

    /// <summary>Makes a project of a folder.</summary>
    void Add(WorkspaceProject project);

    /// <summary>Replaces the project file of a project that is not the primary one: its file was changed.</summary>
    /// <param name="root">The folder of the project.</param>
    /// <param name="project">The project file as it is now.</param>
    void Replace(string root, ProjectFile project);

    /// <summary>The projects that list a file, the one that was loaded first first.</summary>
    /// <param name="path">The absolute path of the file.</param>
    IReadOnlyList<WorkspaceProject> Listing(string path);

    /// <summary>
    /// The role of each folder the client gave, as it is told by what the projects loaded so far list.
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
    private readonly List<(string Project, string RelativeUri)> _refused = [];
    private volatile WorkspaceFolders _folders = new([]);
    private volatile string _primaryRoot = string.Empty;
    private WorkspaceProject? _primaryProject;
    private volatile IReadOnlyList<string> _imported = [];

    public WorkspaceFolders Folders => _folders;

    // the primary project is read live: the project file service holds it, and replaces it as it changes - and the files it lists are worked out again then, not at each ask.
    public IReadOnlyList<WorkspaceProject> Projects
    {
        get
        {
            lock (_sync)
            {
                if (primary.Project is not { } project || _primaryRoot.Length == 0)
                {
                    return [.. _secondary];
                }

                if (_primaryProject is null || !ReferenceEquals(_primaryProject.Project, project) || _primaryProject.Root != _primaryRoot)
                {
                    _primaryProject = new WorkspaceProject(_primaryRoot, project);
                }

                return [_primaryProject, .. _secondary];
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

    public void Initialize(string primaryRoot, IEnumerable<string> folders, IEnumerable<string> importedProjectFolders)
    {
        lock (_sync)
        {
            // the primary project is addressed by the root exactly as it arrived: whatever addresses a symbol derives it from the same string.
            _primaryRoot = primaryRoot;
            _folders = new WorkspaceFolders([primaryRoot, .. folders]);
            _imported = [.. importedProjectFolders];
            _secondary.Clear();
            _primaryProject = null;
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

    public void Replace(string root, ProjectFile project)
    {
        lock (_sync)
        {
            var index = _secondary.FindIndex(candidate => WorkspacePaths.Same(candidate.Root, root));
            if (index >= 0)
            {
                _secondary[index] = new WorkspaceProject(_secondary[index].Root, project);
            }
        }
    }

    public IReadOnlyList<WorkspaceProject> Listing(string path)
    {
        var full = Path.GetFullPath(path);
        return [.. Projects.Where(project => project.ListedFiles.Contains(full))];
    }

    public IReadOnlyList<(string Root, WorkspaceFolderRole Role)> Roles()
    {
        var projects = Projects.ToDictionary(project => WorkspacePaths.Normalized(project.Root), project => project, WorkspacePaths.Comparer);
        return new WorkspaceFolderRoles(_folders, _imported).Roles(root => projects.TryGetValue(root, out var project) ? project.ListedFiles : []);
    }

    public void Refuse(string project, string relativeUri)
    {
        lock (_sync)
        {
            _refused.Add((project, relativeUri));
        }
    }
}
