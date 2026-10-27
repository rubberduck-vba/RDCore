using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Server.Handlers.Workspace;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.LanguageServer.Workspace.States;
using RDCore.Parsing;
using RDCore.SDK.Client;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server.Services.States;
using RDCore.SDK.Workspace;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// A project file the client rewrote while the server runs (<c>workspace/didChangeWatchedFiles</c>): the files it lists now are loaded and parsed, the ones it no
/// longer lists are taken out, the environment host composes its session again, and the workspace is defined again in its three phases.
/// </summary>
[TestClass]
public sealed class WorkspaceReloadServiceTests
{
    // an absolute path the URI ctor accepts on both Windows and the Linux CI runner.
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-reload-ws");

    private readonly MockFileSystem _files = new();
    private readonly IServerStateProvider _state = Substitute.For<IServerStateProvider>();
    private readonly IParsingClientService _parsing = Substitute.For<IParsingClientService>();
    private readonly ISymbolSyncService _symbols = Substitute.For<ISymbolSyncService>();
    private readonly IRDCoreClientApp _host = Substitute.For<IRDCoreClientApp>();
    private readonly DocumentStateProvider _documentStates = new(NullLogger<DocumentStateProvider>.Instance);
    private readonly ProjectFileService _projectFiles;
    private readonly WorkspaceDocumentService _documents;
    private readonly WorkspaceReloadService _sut;

    public WorkspaceReloadServiceTests()
    {
        _state.State.Returns(ServerState.Running);
        _parsing.ParseDocumentAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new ModuleParser().Parse((Uri)call[0], "Public Sub Foo()\r\nEnd Sub")));
        _host.WaitForReadyAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _host.PlatformInfo.Returns(new PlatformInitializeResult { Provided = [nameof(ProjectReload)] });
        HostAnswers(new HostProjectReloadResult { Composed = true });

        var orchestration = Substitute.For<IPlatformOrchestrationService>();
        orchestration.RuntimeEnvironment.Returns(_host);

        _projectFiles = new ProjectFileService(NullLogger<ProjectFileService>.Instance, _files.Path, _files.File, new ProjectFileLoader(_files));
        _documents = new WorkspaceDocumentService(_documentStates, NullLogger<WorkspaceDocumentService>.Instance, _files.Path, _files.File);
        _sut = new WorkspaceReloadService(_state, _projectFiles, _documents, _documentStates, _parsing, _symbols, orchestration, _files.Path,
            NullLogger<WorkspaceReloadService>.Instance);

        _files.AddFile(Path.Combine(Root, "A.bas"), new MockFileData("Attribute VB_Name = \"A\"\r\n"));
        _files.AddFile(Path.Combine(Root, "B.cls"), new MockFileData("Attribute VB_Name = \"B\"\r\n"));
    }

    private void HostAnswers(HostProjectReloadResult result)
        => _host.SendRequestAsync<HostProjectReloadParams, HostProjectReloadResult>(Arg.Any<HostProjectReloadParams>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(result));

    private void WriteProject(params string[] modules)
        => _files.AddFile(Path.Combine(Root, ProjectFile.FileName), new MockFileData(JsonSerializer.Serialize(new ProjectFile(Root, new RDCoreProject
        {
            Name = "Reloaded",
            Modules = [.. modules.Select(module => new RDCoreModule { RelativeUri = module })],
        }))));

    // the workspace as the server loads it when it initializes: the project file read, and each file it lists loaded.
    private async Task LoadAsync(params string[] modules)
    {
        WriteProject(modules);
        await _projectFiles.LoadAsync(Root);
        _documents.Initialize(Root);
        var ids = modules.Select(module => new TextDocumentIdentifier(Path.Combine(Root, module))).ToList();
        _documentStates.Initialize(ids);
        foreach (var id in ids)
        {
            await _documents.TryLoadAsync(id);
        }
    }

    private static Uri UriOf(string module) => new(Path.Combine(Root, module));

    [TestMethod]
    public async Task AModuleTheProjectGained_IsLoadedAndParsed_AndTheWholeWorkspaceIsDefinedAgain()
    {
        await LoadAsync("A.bas");
        WriteProject("A.bas", "B.cls");

        var result = await _sut.ReloadAsync(CancellationToken.None);

        Assert.AreEqual(new WorkspaceReloadResult(Added: 1, Removed: 0, IsHostComposed: true, IsProgramRunning: false), result);
        Assert.IsTrue(_documents.TryGetDocument(UriOf("B.cls"), out _));
        await _parsing.Received(1).ParseDocumentAsync(UriOf("B.cls"), Arg.Any<CancellationToken>());
        await _host.Received(1).SendRequestAsync<HostProjectReloadParams, HostProjectReloadResult>(Arg.Any<HostProjectReloadParams>(), Arg.Any<CancellationToken>());
        await _symbols.Received(1).SyncWorkspaceAsync(Arg.Any<CancellationToken>(), null, true);
    }

    [TestMethod]
    public async Task AModuleTheProjectLost_IsTakenOut_AndTheSessionComposedAgainHoldsNothingOfIt()
    {
        await LoadAsync("A.bas", "B.cls");
        WriteProject("A.bas");

        var result = await _sut.ReloadAsync(CancellationToken.None);

        Assert.AreEqual(new WorkspaceReloadResult(Added: 0, Removed: 1, IsHostComposed: true, IsProgramRunning: false), result);
        Assert.IsFalse(_documents.TryGetDocument(UriOf("B.cls"), out _));
        Assert.IsFalse(_documentStates.IsTracked(new TextDocumentIdentifier(UriOf("B.cls"))));
        await _symbols.DidNotReceiveWithAnyArgs().DiscardModuleAsync(default!, default);
    }

    [TestMethod]
    public async Task AModuleTheProjectLost_IsDiscarded_WhenTheHostKeepsItsSession_AProgramRunningInIt()
    {
        HostAnswers(new HostProjectReloadResult { IsProgramRunning = true });
        await LoadAsync("A.bas", "B.cls");
        WriteProject("A.bas");

        var result = await _sut.ReloadAsync(CancellationToken.None);

        Assert.AreEqual(new WorkspaceReloadResult(Added: 0, Removed: 1, IsHostComposed: false, IsProgramRunning: true), result);
        await _symbols.Received(1).DiscardModuleAsync("B", Arg.Any<CancellationToken>(), false);
        await _symbols.Received(1).SyncWorkspaceAsync(Arg.Any<CancellationToken>(), null, true);
    }

    [TestMethod]
    public async Task AProjectFileThatIsNotValid_LeavesTheWorkspaceAsItWas()
    {
        await LoadAsync("A.bas");
        _files.AddFile(Path.Combine(Root, ProjectFile.FileName), new MockFileData("{ this is not a project"));

        var thrown = false;
        try
        {
            await _sut.ReloadAsync(CancellationToken.None);
        }
        catch (Exception)
        {
            thrown = true;
        }

        Assert.IsTrue(thrown);
        Assert.IsTrue(_documents.TryGetDocument(UriOf("A.bas"), out _));
        Assert.AreEqual("A.bas", _projectFiles.Project.ProjectInfo.Modules.Single().RelativeUri);
        await _symbols.DidNotReceiveWithAnyArgs().SyncWorkspaceAsync(default, default, default);
    }

    [TestMethod]
    public async Task BeforeTheServerRuns_NothingIsReloaded()
    {
        _state.State.Returns(ServerState.Initializing);
        await LoadAsync("A.bas");
        WriteProject("A.bas", "B.cls");

        Assert.IsNull(await _sut.ReloadAsync(CancellationToken.None));
        Assert.IsFalse(_documents.TryGetDocument(UriOf("B.cls"), out _));
    }

    [TestMethod]
    [DataRow(FileChangeType.Changed, true, DisplayName = "the project file, changed")]
    [DataRow(FileChangeType.Created, true, DisplayName = "the project file, created")]
    [DataRow(FileChangeType.Deleted, false, DisplayName = "the project file, deleted")]
    public async Task TheWatchHandler_ReloadsForTheWorkspacesOwnProjectFile_WrittenOnly(FileChangeType type, bool reloads)
    {
        var reload = Substitute.For<IWorkspaceReloadService>();
        await LoadAsync("A.bas");
        var handler = new ProjectFileWatchHandler(reload, _documents, Substitute.For<OmniSharp.Extensions.LanguageServer.Protocol.Server.ILanguageServerFacade>(),
            NullLogger<ProjectFileWatchHandler>.Instance);

        await handler.Handle(new DidChangeWatchedFilesParams
        {
            Changes = new Container<FileEvent>(
                new FileEvent { Uri = DocumentUri.FromFileSystemPath(Path.Combine(Root, ProjectFile.FileName)), Type = type },
                new FileEvent { Uri = DocumentUri.FromFileSystemPath(Path.Combine(Root, "elsewhere", ProjectFile.FileName)), Type = FileChangeType.Changed }),
        }, CancellationToken.None);

        await reload.Received(reloads ? 1 : 0).ReloadAsync(Arg.Any<CancellationToken>());
    }
}
