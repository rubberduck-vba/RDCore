using Microsoft.Extensions.Options;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.Runtime.LanguageCore.Symbols;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Workspace;
using RDCore.Symbols;

namespace RDCore.LanguageServer.Symbols;

/// <summary>
/// What a project of the workspace binds its names among: the libraries it references, and the projects of the workspace it references.
/// </summary>
internal interface IReferencedProjectService
{
    /// <summary>
    /// The symbols a project's names are bound among, addressed under its root: those of the libraries it references
    /// (<see cref="IReferencedLibraryService"/>), then the names each project of the workspace it references gives it
    /// (<see cref="ReferencedProjectSymbols"/>), at the precedence of the reference.
    /// </summary>
    /// <remarks>
    /// The list is the same one for as long as what it is made of is: the libraries, and the parses of the modules of each project referenced. A project that
    /// references none gets the libraries' own list.
    /// </remarks>
    /// <param name="workspaceRoot">The root the project's symbols are addressed under.</param>
    /// <param name="projectRoot">The folder of the project.</param>
    /// <param name="token">Stops the collecting of the modules of a project referenced.</param>
    IReadOnlyList<Symbol> LibrariesOf(Uri workspaceRoot, string projectRoot, CancellationToken token = default);

    /// <summary>
    /// The projects of the workspace a project references, whose names <see cref="LibrariesOf"/> gives it: each reference that names one project of the
    /// workspace other than itself, in the order of the references.
    /// </summary>
    /// <param name="projectRoot">The folder of the project.</param>
    IReadOnlyList<ReferencedProject> ProjectsReferencedBy(string projectRoot) => [];
}

/// <summary>
/// A project of the workspace that a project references.
/// </summary>
/// <param name="Project">The project referenced.</param>
/// <param name="Name">Its name, as the reference names it.</param>
/// <param name="Priority">The reference precedence of the project among the referencing project's references.</param>
internal sealed record class ReferencedProject(WorkspaceProject Project, string Name, int Priority);

/// <inheritdoc cref="IReferencedProjectService"/>
/// <remarks>
/// A reference to a project names it (MS-VBAL §4.1): the one project of the workspace with that name (<see cref="ProjectReferences.Resolve"/>), composed
/// of its modules as they are parsed, among what it references itself. A name no project has, or two projects share, names none. A project on a circular
/// reference of projects is reported (RDC31001); the reference that closes the circle gives no names, and the others give theirs.
/// </remarks>
internal sealed class ReferencedProjectService(
    IReferencedLibraryService libraries,
    IWorkspaceProjects projects,
    IWorkspaceDocumentService documents,
    IParsingClientService parsing,
    ISymbolResolver resolver,
    IOptions<SdkAppOptions> options) : IReferencedProjectService
{
    // a few projects, each composed as a referenced project: those used last.
    private readonly WorkspaceCompositionCache _compositions = new(capacity: 8);
    private readonly object _sync = new();
    private readonly Dictionary<(string Root, string Project), Combined> _combined = [];

    // what a project's list is made of, and the list.
    private sealed record class Combined(IReadOnlyList<Symbol> Libraries, IReadOnlyList<Referenced> Projects, IReadOnlyList<Symbol> Symbols)
    {
        public bool IsMadeOf(IReadOnlyList<Symbol> libraries, IReadOnlyList<Referenced> projects)
            => ReferenceEquals(Libraries, libraries) && Projects.Count == projects.Count
                && Projects.Zip(projects).All(pair => ReferenceEquals(pair.First.Symbols, pair.Second.Symbols) && pair.First.Priority == pair.Second.Priority
                    && string.Equals(pair.First.Name, pair.Second.Name, StringComparison.OrdinalIgnoreCase));
    }

    // a project referenced: its name, its precedence, and the symbols of its composition.
    private sealed record class Referenced(string Name, int Priority, IReadOnlyList<Symbol> Symbols);

    public IReadOnlyList<Symbol> LibrariesOf(Uri workspaceRoot, string projectRoot, CancellationToken token = default)
        => LibrariesOf(workspaceRoot, projectRoot, new HashSet<string>(StringComparer.OrdinalIgnoreCase), token);

    // the projects being composed, each for the one before it, are those a reference to closes a circle.
    private IReadOnlyList<Symbol> LibrariesOf(Uri workspaceRoot, string projectRoot, HashSet<string> composing, CancellationToken token)
    {
        // a document of a server with no workspace is a project of its own, which references what the server's project does.
        if (projectRoot.Length == 0 || documents.WorkspaceRoot.Length == 0)
        {
            return libraries.SymbolsUnder(workspaceRoot);
        }

        var primary = WorkspacePaths.Same(projectRoot, documents.WorkspaceRoot);
        var project = projects.Projects.FirstOrDefault(candidate => WorkspacePaths.Same(candidate.Root, projectRoot))?.Project.ProjectInfo;
        var own = primary || project is null ? libraries.SymbolsUnder(workspaceRoot) : libraries.SymbolsOf(workspaceRoot, project);
        var references = project?.References ?? [];
        if (project is null || !references.Any(reference => reference.IsProject))
        {
            return own;
        }

        var priorities = (primary ? libraries.Libraries : libraries.LibrariesOf(project)).Priorities;
        var name = project.Name ?? string.Empty;
        composing.Add(name);
        var referenced = new List<Referenced>();
        foreach (var reference in references.Where(reference => reference.IsProject))
        {
            if (composing.Contains(reference.Name) || !priorities.TryGetValue(reference.Name, out var priority)
                || SymbolsOf(reference.Name, composing, token) is not { } symbols)
            {
                continue;
            }

            referenced.Add(new Referenced(reference.Name, priority, symbols));
        }

        composing.Remove(name);
        var key = (workspaceRoot.AbsoluteUri, projectRoot);
        lock (_sync)
        {
            if (_combined.TryGetValue(key, out var combined) && combined.IsMadeOf(own, referenced))
            {
                return combined.Symbols;
            }

            IReadOnlyList<Symbol> symbols =
            [
                .. own,
                .. referenced.SelectMany(other => ReferencedProjectSymbols.Of(workspaceRoot, other.Name, other.Symbols, other.Priority)),
            ];
            _combined[key] = new Combined(own, referenced, symbols);
            return symbols;
        }
    }

    public IReadOnlyList<ReferencedProject> ProjectsReferencedBy(string projectRoot)
    {
        if (projectRoot.Length == 0 || documents.WorkspaceRoot.Length == 0
            || projects.Projects.FirstOrDefault(candidate => WorkspacePaths.Same(candidate.Root, projectRoot))?.Project.ProjectInfo is not { } project)
        {
            return [];
        }

        var primary = WorkspacePaths.Same(projectRoot, documents.WorkspaceRoot);
        var priorities = (primary ? libraries.Libraries : libraries.LibrariesOf(project)).Priorities;
        return
        [
            .. (project.References ?? [])
                .Where(reference => reference.IsProject && !string.Equals(reference.Name, project.Name, StringComparison.OrdinalIgnoreCase))
                .Select(reference => (reference, Named: NamedProject(reference.Name)))
                .Where(pair => pair.Named is not null && priorities.ContainsKey(pair.reference.Name))
                .Select(pair => new ReferencedProject(pair.Named!, pair.reference.Name, priorities[pair.reference.Name])),
        ];
    }

    // the one project of the workspace with the name, as every component resolves a reference to a project (ProjectReferences.Resolve); none when no
    // project, or more than one, has it.
    private WorkspaceProject? NamedProject(string projectName)
    {
        var all = projects.Projects;
        var resolution = ProjectReferences.Resolve(
            new RDCoreReference { Name = projectName, IsProject = true }, [.. all.Select(candidate => candidate.Project.ProjectInfo)]);
        return resolution.IsResolved ? all[resolution.Index] : null;
    }

    // the symbols of the composition of the one project of the workspace with the name; none when no project, or more than one, has it.
    private IReadOnlyList<Symbol>? SymbolsOf(string projectName, HashSet<string> composing, CancellationToken token)
    {
        if (NamedProject(projectName) is not { } project)
        {
            return null;
        }

        var root = SymbolUri.Root(WorkspacePaths.Same(project.Root, documents.WorkspaceRoot) ? documents.WorkspaceRoot : project.Root);
        var (_, modules, hosts) = ProjectModules.Collect(documents, parsing, project.Root, root, token);
        var composition = _compositions.GetOrCompose(
            root, [.. modules.Select(module => (module.Uri, module.Kind, module.Parse))], resolver, projectName: project.Project.ProjectInfo.Name,
            implicitScope: options.Value.Workspace.SupportedLanguage.ImplicitDeclarationScope, libraries: LibrariesOf(root, project.Root, composing, token),
            documentHosts: hosts);
        return composition.Symbols ?? [];
    }
}
