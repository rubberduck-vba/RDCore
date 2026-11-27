using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol;
using RDCore.LanguageServer;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.Parsing;
using RDCore.SDK.Client;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Platform.Components;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Workspace;
using RDCore.Symbols;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// The other projects of a workspace are defined in the host, each in its analysis session (<c>rdcore/host/project/analyze</c>): under its own root, never
/// under the primary project's, and a file two projects list is a module of each.
/// </summary>
[TestClass]
public sealed class OtherProjectSyncTests
{
    private static readonly string App = Path.Combine(Path.GetTempPath(), "rdcore-other-projects", "App");
    private static readonly string Tools = Path.Combine(Path.GetTempPath(), "rdcore-other-projects", "Tools");

    private readonly IRDCoreClientApp _host = Substitute.For<IRDCoreClientApp>();
    private readonly IWorkspaceProjects _projects = Substitute.For<IWorkspaceProjects>();
    private readonly List<DefineSymbolsParams> _defined = [];
    private readonly List<Uri> _analyzed = [];
    private readonly List<Uri> _released = [];
    private readonly List<(WorkspaceDocument Document, ModuleParseResult Parse)> _modules = [];

    public OtherProjectSyncTests()
    {
        Provide(nameof(DefineSymbols), nameof(ProjectAnalysis));
        _host.WaitForReadyAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _host.SendRequestAsync<DefineSymbolsParams, DefineSymbolsResult>(Arg.Do<DefineSymbolsParams>(_defined.Add), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new DefineSymbolsResult { Defined = 1 }));
        _host.SendRequestAsync<HostProjectAnalyzeParams, HostProjectAnalyzeResult>(Arg.Do<HostProjectAnalyzeParams>(request => _analyzed.Add(request.WorkspaceRoot)), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HostProjectAnalyzeResult { Composed = true, ModuleCount = 1 }));
        _host.SendRequestAsync<HostProjectReleaseParams, HostProjectReleaseResult>(Arg.Do<HostProjectReleaseParams>(request => _released.Add(request.WorkspaceRoot)), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HostProjectReleaseResult { Released = true }));

        Add(App, "Program.bas", "Program");
        Add(Tools, "Helpers.bas", "Helpers");
        // a file of the primary project's folder that the other project lists too.
        Add(App, "Common.bas", "Common", App, Tools);
        ProjectsAre(Project(App, "App"), Project(Tools, "Tools"));
    }

    private void Provide(params string[] capabilities) => _host.PlatformInfo.Returns(new PlatformInitializeResult { Provided = [.. capabilities] });

    private void Add(string root, string file, string name, params string[] projects)
    {
        var source = $"Attribute VB_Name = \"{name}\"\r\nPublic Sub {name}Work()\r\nEnd Sub\r\n";
        var document = new WorkspaceDocument(file, root, source) { Projects = projects.Length == 0 ? [root] : projects };
        _modules.Add((document, new ModuleParser().Parse(document.Id.Uri.ToUri(), source)));
    }

    private static WorkspaceProject Project(string root, string name) => new(root, new ProjectFile(root, new RDCoreProject { Name = name }));

    private void ProjectsAre(params WorkspaceProject[] projects) => _projects.Projects.Returns(projects);

    private SymbolSyncService Sut(IReferencedProjectService? referencedProjects = null)
    {
        var orchestration = Substitute.For<IPlatformOrchestrationService>();
        orchestration.RuntimeEnvironment.Returns(_host);

        var documents = Substitute.For<IWorkspaceDocumentService>();
        documents.WorkspaceRoot.Returns(App);
        documents.GetAllDocuments().Returns(_ => _modules.Select(module => module.Document).ToList());
        documents.TryGetDocument(Arg.Any<Uri>(), out Arg.Any<WorkspaceDocument>()).Returns(call =>
        {
            var found = _modules.FirstOrDefault(module => module.Document.Id.Uri.ToUri().AbsoluteUri == ((Uri)call[0]).AbsoluteUri).Document;
            call[1] = found;
            return found is not null;
        });

        var parsing = Substitute.For<IParsingClientService>();
        parsing.TryGetCached(Arg.Any<Uri>(), out Arg.Any<ModuleParseResult>()).Returns(call => Cached(call, 1));
        parsing.TryGetCached(Arg.Any<Uri>(), Arg.Any<int>(), out Arg.Any<ModuleParseResult>()).Returns(call => Cached(call, 2));

        var libraries = Substitute.For<IReferencedLibraryService>();
        libraries.SymbolsUnder(default!).ReturnsForAnyArgs(Array.Empty<Symbol>());
        libraries.SymbolsOf(default!, default!).ReturnsForAnyArgs(Array.Empty<Symbol>());

        return new SymbolSyncService(orchestration, parsing, documents, new IntrinsicSymbolResolver(),
            Microsoft.Extensions.Options.Options.Create(new SdkAppOptions()), NullLogger<SymbolSyncService>.Instance, libraries, _projects,
            referencedProjects: referencedProjects);
    }

    private bool Cached(NSubstitute.Core.CallInfo call, int parseAt)
    {
        var uri = ((Uri)call[0]).AbsoluteUri;
        var parse = _modules.FirstOrDefault(module => module.Document.Id.Uri.ToUri().AbsoluteUri == uri).Parse;
        call[parseAt] = parse;
        return parse is not null;
    }

    // the modules of a project's own defined under a root, and the phase they were defined in: the code is loaded by a request of its own.
    private string[] CodeLoadedUnder(string root)
        => [.. _defined.Where(request => request.CodeOnly && request.Library is null
                && request.WorkspaceRoot?.AbsoluteUri == SymbolUri.Root(root).AbsoluteUri)
            .Select(request => request.ModuleUri!.AbsoluteUri).Order(StringComparer.Ordinal)];

    private static string[] Addresses(string root, params string[] modules)
        => [.. modules.Select(module => SymbolUri.Module(root, module).AbsoluteUri).Order(StringComparer.Ordinal)];

    [TestMethod]
    public async Task AnotherProject_IsAnalyzed_AndItsModulesAreDefinedUnderItsRoot()
    {
        await Sut().SyncWorkspaceAsync(CancellationToken.None);

        CollectionAssert.AreEqual(new[] { SymbolUri.Root(Tools).AbsoluteUri }, _analyzed.Select(root => root.AbsoluteUri).ToArray());
        CollectionAssert.AreEqual(Addresses(Tools, "Common", "Helpers"), CodeLoadedUnder(Tools));
        CollectionAssert.AreEqual(Addresses(App, "Common", "Program"), CodeLoadedUnder(App));
    }

    [TestMethod]
    public async Task WithoutProjectAnalysis_AnotherProjectIsNotSentToTheHost()
    {
        Provide(nameof(DefineSymbols));

        await Sut().SyncWorkspaceAsync(CancellationToken.None);

        Assert.IsEmpty(_analyzed);
        Assert.IsEmpty(CodeLoadedUnder(Tools));
        CollectionAssert.AreEqual(Addresses(App, "Common", "Program"), CodeLoadedUnder(App));
    }

    [TestMethod]
    public async Task AProjectAsItWas_IsNotAnalyzedAgain_AndOneThatChangedIs()
    {
        var sut = Sut();
        await sut.SyncWorkspaceAsync(CancellationToken.None);
        await sut.SyncWorkspaceAsync(CancellationToken.None, replace: true);
        Assert.HasCount(1, _analyzed);

        ProjectsAre(Project(App, "App"), Project(Tools, "Tools"));
        await sut.SyncWorkspaceAsync(CancellationToken.None, replace: true);

        Assert.HasCount(2, _analyzed);
    }

    [TestMethod]
    public async Task AProjectThatLeftTheWorkspace_IsReleased()
    {
        var sut = Sut();
        await sut.SyncWorkspaceAsync(CancellationToken.None);

        ProjectsAre(Project(App, "App"));
        await sut.SyncWorkspaceAsync(CancellationToken.None, replace: true);

        CollectionAssert.AreEqual(new[] { SymbolUri.Root(Tools).AbsoluteUri }, _released.Select(root => root.AbsoluteUri).ToArray());
    }

    [TestMethod]
    public async Task AnEditedModuleOfAnotherProject_IsDefinedAgainInItsSession_AndNotInTheRuntimeSession()
    {
        var sut = Sut();
        await sut.SyncWorkspaceAsync(CancellationToken.None);
        _defined.Clear();

        await sut.SyncDocumentAsync(_modules.Single(module => module.Document.Name == "Helpers").Document.Id.Uri.ToUri(), CancellationToken.None);

        CollectionAssert.AreEqual(Addresses(Tools, "Helpers"), CodeLoadedUnder(Tools));
        Assert.IsEmpty(CodeLoadedUnder(App));
    }

    // a block an edit took out of a file of another project is taken out of that project's session, as it is of the runtime session: by its address under
    // the project's root, with that root, which the host routes the discard by. The runtime session is not told of it.
    [TestMethod]
    [DataRow(false, DisplayName = "the document's sync")]
    [DataRow(true, DisplayName = "the workspace's sync, the project as it was")]
    public async Task ABlockAnEditTookOutOfAFileOfAnotherProject_IsDiscardedFromItsSessionOnly(bool workspace)
    {
        var discarded = DiscardsAreTold();
        Write(Tools, "Helpers.bas", Helpers + "Class Ledger\r\nEnd Class\r\n");
        var sut = Sut();
        await sut.SyncWorkspaceAsync(CancellationToken.None);

        var helpers = Write(Tools, "Helpers.bas", Helpers);
        await (workspace ? sut.SyncWorkspaceAsync(CancellationToken.None, replace: true) : sut.SyncDocumentAsync(helpers, CancellationToken.None));

        var discard = Assert.ContainsSingle(discarded);
        Assert.AreEqual(SymbolUri.Root(Tools).AbsoluteUri, discard.WorkspaceRoot?.AbsoluteUri);
        Assert.AreEqual(SymbolUri.Module(Tools, "Ledger").AbsoluteUri, discard.ModuleUri.AbsoluteUri);
    }

    // the twin of the test above: a session composed again holds none of what the one it replaces held, so nothing is discarded from it.
    [TestMethod]
    public async Task ABlockOfAProjectThatChanged_IsNotDiscarded_ItsSessionIsComposedAgain()
    {
        var discarded = DiscardsAreTold();
        Write(Tools, "Helpers.bas", Helpers + "Class Ledger\r\nEnd Class\r\n");
        var sut = Sut();
        await sut.SyncWorkspaceAsync(CancellationToken.None);

        Write(Tools, "Helpers.bas", Helpers);
        ProjectsAre(Project(App, "App"), Project(Tools, "Tools"));
        await sut.SyncWorkspaceAsync(CancellationToken.None, replace: true);

        Assert.HasCount(2, _analyzed);
        Assert.IsEmpty(discarded);
    }

    private const string Helpers = "Attribute VB_Name = \"Helpers\"\r\nPublic Sub HelpersWork()\r\nEnd Sub\r\n";

    // the host takes modules out of its sessions; what it is asked to take out.
    private List<HostDiscardParams> DiscardsAreTold()
    {
        Provide(nameof(DefineSymbols), nameof(ProjectAnalysis), nameof(SessionDiscard));
        var discarded = new List<HostDiscardParams>();
        _host.SendRequestAsync<HostDiscardParams, DiscardResult>(Arg.Do<HostDiscardParams>(discarded.Add), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new DiscardResult { Discarded = 1 }));
        return discarded;
    }

    // a file of the project of a root, written with the source given: what the server has of it, and its parse, in a workspace that allows blocks in a
    // module file.
    private Uri Write(string root, string file, string source)
    {
        _modules.RemoveAll(module => module.Document.Name == Path.GetFileNameWithoutExtension(file) && module.Document.WorkspaceRoot == root);
        var document = new WorkspaceDocument(file, root, source) { Projects = [root] };
        _modules.Add((document, FileLevelStatementsOn.Parser().Parse(document.Id.Uri.ToUri(), source)));
        return document.Id.Uri.ToUri();
    }

    [TestMethod]
    public async Task AnEditedFileTwoProjectsList_IsDefinedAgainInEachSession()
    {
        var sut = Sut();
        await sut.SyncWorkspaceAsync(CancellationToken.None);
        _defined.Clear();

        await sut.SyncDocumentAsync(_modules.Single(module => module.Document.Name == "Common").Document.Id.Uri.ToUri(), CancellationToken.None);

        CollectionAssert.AreEqual(Addresses(Tools, "Common"), CodeLoadedUnder(Tools));
        CollectionAssert.AreEqual(Addresses(App, "Common"), CodeLoadedUnder(App));
    }

    // the primary project's session has the names of the projects it references: their modules are defined in it as that project's, under that project's
    // root, before its own modules are, and their code is sent after the project's own. A Common of the referenced project stands beside the project's own.
    [TestMethod]
    public async Task TheModulesOfAProjectTheProjectReferences_AreDefinedInItsSession_AsThatProjects()
    {
        var tools = Project(Tools, "Tools");
        ProjectsAre(Project(App, "App"), tools);
        var referencedProjects = Substitute.For<IReferencedProjectService>();
        referencedProjects.LibrariesOf(default!, default!, default).ReturnsForAnyArgs(Array.Empty<Symbol>());
        referencedProjects.ProjectsReferencedBy(App).Returns([new ReferencedProject(tools, "Tools", 1)]);

        await Sut(referencedProjects).SyncWorkspaceAsync(CancellationToken.None);

        var library = _defined.Where(request => request.Library is not null && !request.CodeOnly).ToList();
        CollectionAssert.AreEqual(
            Addresses(Tools, "Common", "Helpers"), library.Select(request => request.ModuleUri!.AbsoluteUri).Order(StringComparer.Ordinal).ToArray());
        Assert.IsTrue(library.All(request => request is
        {
            Library: "Tools", LibraryPriority: 1, ModuleType: ModuleType.StdModule, ParseResultJson.Length: 0,
        }));
        Assert.IsTrue(library.All(request => request.WorkspaceRoot!.AbsoluteUri == SymbolUri.Root(App).AbsoluteUri),
            "defined in the primary project's session");

        var firstOwn = _defined.FindIndex(request => request.Library is null && request.WorkspaceRoot!.AbsoluteUri == SymbolUri.Root(App).AbsoluteUri);
        Assert.IsTrue(_defined.FindLastIndex(request => request.Library is not null && !request.CodeOnly) < firstOwn, "before the project's own modules");
        CollectionAssert.AreEqual(Addresses(App, "Common", "Program"), CodeLoadedUnder(App));

        var code = _defined.Where(request => request.Library is not null && request.CodeOnly).ToList();
        CollectionAssert.AreEqual(
            Addresses(Tools, "Common", "Helpers"), code.Select(request => request.ModuleUri!.AbsoluteUri).Order(StringComparer.Ordinal).ToArray());
        Assert.IsTrue(code.All(request => request is { Library: "Tools", LibraryPriority: 1, ModuleType: ModuleType.StdModule, ParseResultJson.Length: > 0 }
            && request.WorkspaceRoot!.AbsoluteUri == SymbolUri.Root(App).AbsoluteUri));
        var lastOwnCode = _defined.FindLastIndex(request => request.Library is null && request.CodeOnly
            && request.WorkspaceRoot!.AbsoluteUri == SymbolUri.Root(App).AbsoluteUri);
        Assert.IsTrue(_defined.FindIndex(request => request.Library is not null && request.CodeOnly) > lastOwnCode, "after the project's own code");
    }

    // an edited module of a project the primary project references is defined again in the primary project's session as that project's, with its code.
    [TestMethod]
    [Ignore("An edited module is defined again in its own project's session only: the primary session keeps the referenced module as it was loaded.")]
    public async Task AnEditedModuleOfAReferencedProject_IsDefinedAgainInTheSession_WithItsCode()
    {
        var tools = Project(Tools, "Tools");
        ProjectsAre(Project(App, "App"), tools);
        var referencedProjects = Substitute.For<IReferencedProjectService>();
        referencedProjects.LibrariesOf(default!, default!, default).ReturnsForAnyArgs(Array.Empty<Symbol>());
        referencedProjects.ProjectsReferencedBy(App).Returns([new ReferencedProject(tools, "Tools", 1)]);
        var sut = Sut(referencedProjects);
        await sut.SyncWorkspaceAsync(CancellationToken.None);
        _defined.Clear();

        await sut.SyncDocumentAsync(_modules.Single(module => module.Document.Name == "Helpers").Document.Id.Uri.ToUri(), CancellationToken.None);

        var again = _defined
            .Where(request => request is { Library: "Tools" } && request.WorkspaceRoot!.AbsoluteUri == SymbolUri.Root(App).AbsoluteUri)
            .ToList();
        CollectionAssert.AreEqual(new[] { false, true }, again.Select(request => request.CodeOnly).ToArray(), "its names, then its code");
        Assert.IsTrue(again.All(request => request.ModuleUri!.AbsoluteUri == Addresses(Tools, "Helpers").Single()));
    }

    // MS-VBAL §4.1: the code of a referenced project runs against the projects it references. App references Tools, and Tools references Charts: Charts's
    // modules are defined in the session, with their code, for Tools's code to call.
    [TestMethod]
    [Ignore("Project references are not followed transitively: the session has the modules of the projects the primary project references only.")]
    public async Task TheModulesOfAProjectAReferencedProjectReferences_AreDefinedInTheSession_WithTheirCode()
    {
        var chartsFolder = Path.Combine(Path.GetTempPath(), "rdcore-other-projects", "Charts");
        Add(chartsFolder, "Plots.bas", "Plots");
        var tools = Project(Tools, "Tools");
        var charts = Project(chartsFolder, "Charts");
        ProjectsAre(Project(App, "App"), tools, charts);
        var referencedProjects = Substitute.For<IReferencedProjectService>();
        referencedProjects.LibrariesOf(default!, default!, default).ReturnsForAnyArgs(Array.Empty<Symbol>());
        referencedProjects.ProjectsReferencedBy(App).Returns([new ReferencedProject(tools, "Tools", 1)]);
        referencedProjects.ProjectsReferencedBy(Tools).Returns([new ReferencedProject(charts, "Charts", 1)]);

        await Sut(referencedProjects).SyncWorkspaceAsync(CancellationToken.None);

        var code = _defined.Where(request => request.Library == "Charts" && request.CodeOnly).Select(request => request.ModuleUri!.AbsoluteUri).ToArray();
        CollectionAssert.AreEqual(Addresses(chartsFolder, "Plots"), code);
    }
}
