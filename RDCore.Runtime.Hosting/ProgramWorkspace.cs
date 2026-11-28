using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Workspace;
using RDCore.SDK.Workspace.Forms;
using System.IO.Abstractions;

namespace RDCore.Runtime.Hosting;

/// <summary>
/// A module of a program's workspace: its name and kind, as the module says them, and where it is.
/// </summary>
/// <param name="Name">The name the module declares (<c>Attribute VB_Name</c>), or its file name when it declares none.</param>
/// <param name="Type">A standard module or a class module, as its header says.</param>
/// <param name="RelativePath">Where the module is, relative to the root of the workspace.</param>
/// <param name="Source">The source of the module.</param>
/// <param name="Companion">
/// The content of the companion file its source names (a form's <c>.frx</c>), beside it; empty when it names none, when the file is not there, and when it
/// cannot be read.
/// </param>
/// <param name="CompanionProblem">Why the companion file could not be read; null when it was, when the module names none, and when the file is not there.</param>
internal sealed record class ProgramWorkspaceModule(string Name, ModuleType Type, string RelativePath, string Source, ReadOnlyMemory<byte> Companion = default,
    Exception? CompanionProblem = null)
{
    /// <summary>
    /// Every module the file declares (<see cref="ModuleKinds.ModulesOf(RDCoreModule, string?, SdkWorkspaceOptions)"/>): its own, which
    /// <see cref="Name"/> and <see cref="Type"/> are, then each block's.
    /// </summary>
    public IReadOnlyList<DeclaredModule> Declared { get; init; } = [new DeclaredModule(Name, Type, IsBlock: false)];
}

/// <summary>
/// A project of the workspace that the program's project references (<strong>RD-VBAL §2.2.3.2</strong>).
/// </summary>
/// <param name="Name">The name of the project, as the reference gives it.</param>
/// <param name="Workspace">The project's folder, read as a workspace of its own: its modules and its project file.</param>
internal sealed record class ReferencedProgramWorkspace(string Name, ProgramWorkspace Workspace);

/// <summary>
/// The code of a program, as <c>rdc bench</c>, <c>rdc run</c> and a composer read it: the modules of a workspace, and the project they make.
/// </summary>
/// <remarks>
/// A folder with a <c>.rdproj</c> is the project it describes: its modules, its references and its precompiler constants. A folder with none is the project
/// the language server makes of it (<see cref="IProjectFileLoader.LoadOrSynthesizeAsync"/>): every file under it that is written in the language, and a single
/// file is a project of that one module.
/// </remarks>
/// <param name="Root">The absolute path of the root of the workspace.</param>
/// <param name="Project">The project: what the session is composed from.</param>
/// <param name="Modules">The modules, in the order the project lists them.</param>
internal sealed record class ProgramWorkspace(string Root, RDCoreProject Project, IReadOnlyList<ProgramWorkspaceModule> Modules)
{
    /// <summary>
    /// The projects of the workspace the project references, in the order of its references: each one's name as the reference gives it, and its folder
    /// read as a workspace of its own. Its own references are not read.
    /// </summary>
    public IReadOnlyList<ReferencedProgramWorkspace> ReferencedProjects { get; init; } = [];

    /// <summary>
    /// The module of the file the workspace was loaded from, when that file is a module of the project of its folder (<see cref="LoadAsync"/>);
    /// <see langword="null"/> when the workspace was loaded from a folder, or from a file that is a project of its own.
    /// </summary>
    public ProgramWorkspaceModule? Given { get; init; }

    /// <summary>
    /// The root of the workspace as its <c>file</c> URI (<see cref="SymbolUri.Root"/>): what its modules are addressed under.
    /// </summary>
    public Uri RootUri => SymbolUri.Root(Root);

    /// <summary>
    /// The <c>file</c> URI of a module of the workspace: the document its parse, and the instructions made of it, say they are written in.
    /// </summary>
    /// <remarks>
    /// Written as its URI, never as its path, as <see cref="SymbolUri.Root"/> writes a root: a parse travels as JSON, and the text of a path that begins with
    /// <c>/</c> is read back as a relative URI.
    /// </remarks>
    /// <param name="module">A module of the workspace.</param>
    public Uri DocumentOf(ProgramWorkspaceModule module) => new(new Uri(Path.Combine(Root, module.RelativePath)).AbsoluteUri);

    /// <summary>
    /// The workspace at <paramref name="path"/>: a module file, or a folder.
    /// </summary>
    /// <param name="fileSystem">The file system the workspace is on.</param>
    /// <param name="projects">Reads a <c>.rdproj</c>, and makes the project of a folder that has none.</param>
    /// <param name="path">The file or the folder.</param>
    /// <param name="token">A token that cancels the reading.</param>
    /// <param name="language">The language whose files are the modules of a folder that has no <c>.rdproj</c>; RD-VBA when it is not given.</param>
    /// <param name="ansiCodePage">
    /// The code page of a module file that has no byte order mark and is not valid UTF-8 (<see cref="ModuleFileText.Decode(byte[], int)"/>): the
    /// environment's.
    /// </param>
    /// <param name="inItsProject">
    /// Whether a module file that the <c>.rdproj</c> of its folder lists is loaded as that project, the file's module being <see cref="Given"/>, as
    /// <c>rdc run</c> and <c>rdc build</c> load it; a module file is a project of its own otherwise.
    /// </param>
    /// <param name="workspace">
    /// The workspace's settings, which say the files that declare modules in blocks
    /// (<see cref="ModuleKinds.ModulesOf(RDCoreModule, string?, SdkWorkspaceOptions)"/>); the defaults when they are not given.
    /// </param>
    /// <exception cref="FileNotFoundException">There is no such file or folder.</exception>
    /// <exception cref="InvalidOperationException">A reference of the project to a project names no project of the workspace, or more than one.</exception>
    public static async Task<ProgramWorkspace> LoadAsync(IFileSystem fileSystem, IProjectFileLoader projects, string path, CancellationToken token = default,
        SupportedLanguage? language = null, int ansiCodePage = ModuleFileText.DefaultAnsiCodePage, bool inItsProject = false,
        SdkWorkspaceOptions? workspace = null)
    {
        workspace ??= new SdkWorkspaceOptions();
        var full = fileSystem.Path.GetFullPath(path);
        if (fileSystem.File.Exists(full))
        {
            var root = fileSystem.Path.GetDirectoryName(full)!;
            if (inItsProject && projects.TryLocate(root, out _))
            {
                var folder = await LoadAsync(fileSystem, projects, root, token, language, ansiCodePage, workspace: workspace);
                if (folder.Modules.FirstOrDefault(module => WorkspacePaths.Same(fileSystem.Path.Combine(root, module.RelativePath), full)) is { } given)
                {
                    return folder with { Given = given };
                }
            }

            var name = fileSystem.Path.GetFileNameWithoutExtension(full);
            var module = new RDCoreModule { RelativeUri = fileSystem.Path.GetFileName(full) };
            return Compose(fileSystem, root, new RDCoreProject { Name = name }, [module], ansiCodePage, workspace);
        }

        if (!fileSystem.Directory.Exists(full))
        {
            throw new FileNotFoundException($"'{path}' is neither a module file nor a folder.", path);
        }

        // a folder with no .rdproj is a project of its own, with no references.
        var project = (await projects.LoadOrSynthesizeAsync(full, language ?? SupportedLanguages.RDVBA, token)).ProjectInfo;
        return Compose(fileSystem, full, project, project.Modules, ansiCodePage, workspace) with
        {
            ReferencedProjects = projects.TryLocate(full, out _)
                ? await ReferencedAsync(fileSystem, projects, full, project, ansiCodePage, workspace, token)
                : [],
        };
    }

    // RD-VBAL §2.2.3.2: a reference to a project names the one project of the workspace with that name (ProjectReferences), among the folders the
    // .rdworkspace lists (WorkspaceFolderList.Read); with no .rdworkspace, the project is the workspace's only one. A reference that names no project, or
    // more than one, refuses the load, naming it. A reference to the project itself closes a circle of references, and gives no names.
    private static async Task<IReadOnlyList<ReferencedProgramWorkspace>> ReferencedAsync(
        IFileSystem fileSystem, IProjectFileLoader projects, string root, RDCoreProject project, int ansiCodePage, SdkWorkspaceOptions options,
        CancellationToken token)
    {
        if (!project.ProjectReferences.Any())
        {
            return [];
        }

        var workspace = new List<(string Root, RDCoreProject Project)>();
        foreach (var folder in WorkspaceFolderList.Read(fileSystem, root)?.Folders ?? [])
        {
            if (WorkspacePaths.Same(folder.Path, root))
            {
                workspace.Add((root, project));
            }
            else if (projects.TryLocate(folder.Path, out _))
            {
                workspace.Add((folder.Path, (await projects.LoadAsync(folder.Path, token)).ProjectInfo));
            }
        }

        if (!workspace.Any(each => WorkspacePaths.Same(each.Root, root)))
        {
            workspace.Insert(0, (root, project));
        }

        var resolutions = ProjectReferences.Of(project, [.. workspace.Select(each => each.Project)]);
        var refused = resolutions.Where(resolution => !resolution.IsResolved).Select(resolution => resolution.ToMessage(project.Name)).ToArray();
        if (refused.Length > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, refused));
        }

        var referenced = new List<ReferencedProgramWorkspace>();
        foreach (var resolution in resolutions)
        {
            var (folder, info) = workspace[resolution.Index];
            var again = referenced.Any(each => string.Equals(each.Name, resolution.Reference.Name, StringComparison.OrdinalIgnoreCase));
            if (WorkspacePaths.Same(folder, root) || again)
            {
                continue;
            }

            referenced.Add(new ReferencedProgramWorkspace(resolution.Reference.Name, Compose(fileSystem, folder, info, info.Modules, ansiCodePage, options)));
        }

        return referenced;
    }

    // the project lists the modules by where they are, and says which are document modules; the session reads the same files, in their own encoding, a form's
    // companion file with its module as the language server reads it, and the standard library is referenced first, as it always is.
    private static ProgramWorkspace Compose(
        IFileSystem fileSystem, string root, RDCoreProject project, IReadOnlyList<RDCoreModule> listed, int ansiCodePage, SdkWorkspaceOptions workspace)
    {
        var modules = listed.Select(module =>
        {
            var path = fileSystem.Path.Combine(root, module.RelativeUri);
            var source = ModuleFileText.Decode(fileSystem.File.ReadAllBytes(path), ansiCodePage).Text;
            var companion = FormDesigner.ReadCompanion(fileSystem, path, source, out var problem);
            var declared = ModuleKinds.ModulesOf(module, source, workspace);
            return new ProgramWorkspaceModule(declared[0].Name, declared[0].Kind, module.RelativeUri, source, companion, problem) { Declared = declared };
        }).ToArray();

        // the standard library is every project's own, and no reference of it.
        return new ProgramWorkspace(root, project with
        {
            Name = project.Name is { Length: > 0 } ? project.Name : "Bench",
            Modules = [.. listed.Select(module => new RDCoreModule { RelativeUri = module.RelativeUri, Super = module.Super })],
            References = [.. project.References.Where(reference => !reference.IsStandardLibrary)],
        }, modules);
    }
}
