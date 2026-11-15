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

    private SymbolSyncService Sut()
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
            Microsoft.Extensions.Options.Options.Create(new SdkAppOptions()), NullLogger<SymbolSyncService>.Instance, libraries, _projects);
    }

    private bool Cached(NSubstitute.Core.CallInfo call, int parseAt)
    {
        var uri = ((Uri)call[0]).AbsoluteUri;
        var parse = _modules.FirstOrDefault(module => module.Document.Id.Uri.ToUri().AbsoluteUri == uri).Parse;
        call[parseAt] = parse;
        return parse is not null;
    }

    // the modules defined under a root, and the phase they were defined in: the code is loaded by a request of its own.
    private string[] CodeLoadedUnder(string root)
        => [.. _defined.Where(request => request.CodeOnly && request.WorkspaceRoot?.AbsoluteUri == SymbolUri.Root(root).AbsoluteUri)
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
}
