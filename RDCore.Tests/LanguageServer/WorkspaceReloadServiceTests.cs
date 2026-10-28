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

        _projectFiles = new ProjectFileService(NullLogger<ProjectFileService>.Instance, _files.Path, _files.File, new ProjectFileLoader(_files),
            () => SupportedLanguages.RDVBA);
        _documents = new WorkspaceDocumentService(_documentStates, NullLogger<WorkspaceDocumentService>.Instance, _files.Path, _files.File);
        _sut = new WorkspaceReloadService(_state, _projectFiles, _documents, _documentStates, _parsing, _symbols, orchestration, _files.Path,
            NullLogger<WorkspaceReloadService>.Instance);

        _files.AddFile(Path.Combine(Root, "A.bas"), new MockFileData("Attribute VB_Name = \"A\"\r\n"));
        _files.AddFile(Path.Combine(Root, "B.cls"), new MockFileData("Attribute VB_Name = \"B\"\r\n"));
    }

    private void HostAnswers(HostProjectReloadResult result)
        => _host.SendRequestAsync<HostProjectReloadParams, HostProjectReloadResult>(Arg.Any<HostProjectReloadParams>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(result));

    private void WriteProject(params string[] modules) => WriteProject(modules, others: []);

    private void WriteProject(string[] modules, string[] others)
        => _files.AddFile(Path.Combine(Root, ProjectFile.FileName), new MockFileData(JsonSerializer.Serialize(new ProjectFile(Root, new RDCoreProject
        {
            Name = "Reloaded",
            Modules = [.. modules.Select(module => new RDCoreModule { RelativeUri = module })],
            OtherFiles = [.. others.Select(other => new RDCoreFile { RelativeUri = other })],
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

    // a resource file the project gained is a document of the workspace, and none of its code: it is never parsed (HexIDE, desk 2026-10-27).
    [TestMethod]
    public async Task AnOtherFileTheProjectGained_IsLoaded_AndNotParsed()
    {
        _files.AddFile(Path.Combine(Root, "res", "App.res"), new MockFileData([0x00, 0x00, 0x20, 0x00, 0xFF, 0xFF]));
        await LoadAsync("A.bas");
        WriteProject(["A.bas"], ["res/App.res"]);

        await _sut.ReloadAsync(CancellationToken.None);

        Assert.IsTrue(_documents.TryGetDocument(UriOf(Path.Combine("res", "App.res")), out _));
        CollectionAssert.AreEqual(new[] { "A.bas" }, _documents.GetAllDocuments().Select(document => document.RelativePath).ToArray());
        await _parsing.DidNotReceive().ParseDocumentAsync(UriOf(Path.Combine("res", "App.res")), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task AnOtherFileTheProjectMadeAModule_IsCodeAgain()
    {
        await LoadAsync("A.bas");
        WriteProject(["A.bas"], ["B.cls"]);
        await _sut.ReloadAsync(CancellationToken.None);
        WriteProject("A.bas", "B.cls");

        await _sut.ReloadAsync(CancellationToken.None);

        CollectionAssert.AreEquivalent(new[] { "A.bas", "B.cls" }, _documents.GetAllDocuments().Select(document => document.RelativePath).ToArray());
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
    [DataRow(InitializeTrace.Off, DisplayName = "traced nothing (a client that asks for no trace)")]
    [DataRow(InitializeTrace.Messages, DisplayName = "traced messages")]
    [DataRow(InitializeTrace.Verbose, DisplayName = "traced verbosely")]
    public async Task AServerThatRuns_Reloads_WhateverTheTraceTheClientAskedFor(InitializeTrace trace)
    {
        _state.State.Returns(trace switch
        {
            InitializeTrace.Off => new RunningTracelessServerState(),
            InitializeTrace.Verbose => new RunningVerboseServerState(),
            _ => ServerState.Running,
        });
        await LoadAsync("A.bas");
        WriteProject("A.bas", "B.cls");

        var result = await _sut.ReloadAsync(CancellationToken.None);

        Assert.AreEqual(1, result?.Added);
    }

    [TestMethod]
    public async Task AProjectFileThatListsWhatTheWorkspaceIs_ChangesNothing()
    {
        await LoadAsync("A.bas");
        WriteProject("A.bas");

        var result = await _sut.ReloadAsync(CancellationToken.None);

        Assert.AreSame(WorkspaceReloadResult.Unchanged, result);
        await _host.DidNotReceiveWithAnyArgs().SendRequestAsync<HostProjectReloadParams, HostProjectReloadResult>(default!, default);
        await _symbols.DidNotReceiveWithAnyArgs().SyncWorkspaceAsync(default, default, default);
    }

    [TestMethod]
    public async Task AChange_IsWrittenToTheProjectFile_AndTheWorkspaceIsWhatItLists_AndReadingItBackChangesNothing()
    {
        await LoadAsync("A.bas");

        var result = await _sut.ChangeAsync(project => project.WithModule(new RDCoreModule { RelativeUri = "B.cls" }), CancellationToken.None);

        Assert.AreEqual(new WorkspaceReloadResult(Added: 1, Removed: 0, IsHostComposed: true, IsProgramRunning: false), result);
        var written = JsonSerializer.Deserialize<ProjectFile>(_files.File.ReadAllText(Path.Combine(Root, ProjectFile.FileName)))!;
        CollectionAssert.AreEqual(new[] { "A.bas", "B.cls" }, written.ProjectInfo.Modules.Select(module => module.RelativeUri).ToArray());
        Assert.IsTrue(_documents.TryGetDocument(UriOf("B.cls"), out _));

        // the client that watches the project file says it changed: it is the server's own write.
        Assert.AreSame(WorkspaceReloadResult.Unchanged, await _sut.ReloadAsync(CancellationToken.None));
        await _symbols.Received(1).SyncWorkspaceAsync(Arg.Any<CancellationToken>(), null, true);
    }

    // a project file that cannot be written leaves the project what it was - what is on disk, and what the workspace is (HexIDE, desk #32).
    [TestMethod]
    public async Task AChangeThatCannotBeWritten_LeavesTheProjectAsItWas()
    {
        await LoadAsync("A.bas");
        _files.File.SetAttributes(Path.Combine(Root, ProjectFile.FileName), FileAttributes.ReadOnly);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _sut.ChangeAsync(project => project.WithModule(new RDCoreModule { RelativeUri = "B.cls" }), CancellationToken.None));

        CollectionAssert.AreEqual(new[] { "A.bas" }, _projectFiles.Project.ProjectInfo.Modules.Select(module => module.RelativeUri).ToArray());
        Assert.IsFalse(_documents.TryGetDocument(UriOf("B.cls"), out _));
    }

    [TestMethod]
    public async Task AChangeThatLeavesTheProjectAsItWas_WritesNothing()
    {
        await LoadAsync("A.bas");
        var before = _files.File.ReadAllText(Path.Combine(Root, ProjectFile.FileName));

        var result = await _sut.ChangeAsync(project => project, CancellationToken.None);

        Assert.AreSame(WorkspaceReloadResult.Unchanged, result);
        Assert.AreEqual(before, _files.File.ReadAllText(Path.Combine(Root, ProjectFile.FileName)));
    }

    [TestMethod]
    public async Task AFolderWithNoProjectFile_IsLoadedAsTheProjectItHolds_AndWrittenOnItsFirstChange()
    {
        await _projectFiles.LoadAsync(Root);
        _documents.Initialize(Root);

        Assert.IsTrue(_projectFiles.Project.IsSynthesized);
        CollectionAssert.AreEqual(new[] { "A.bas", "B.cls" }, _projectFiles.Project.ProjectInfo.Modules.Select(module => module.RelativeUri).ToArray());
        Assert.IsFalse(_files.FileExists(Path.Combine(Root, ProjectFile.FileName)));

        await _sut.ChangeAsync(project => project.WithoutModule(project.ProjectInfo.Modules[1]), CancellationToken.None);

        Assert.IsFalse(_projectFiles.Project.IsSynthesized);
        var written = JsonSerializer.Deserialize<ProjectFile>(_files.File.ReadAllText(Path.Combine(Root, ProjectFile.FileName)))!;
        CollectionAssert.AreEqual(new[] { "A.bas" }, written.ProjectInfo.Modules.Select(module => module.RelativeUri).ToArray());
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
    public async Task TheWatchedFileChanges_ReloadForTheWorkspacesOwnProjectFile_WrittenOnly(FileChangeType type, bool reloads)
    {
        var reload = Substitute.For<IWorkspaceReloadService>();
        await LoadAsync("A.bas");
        var changes = new WatchedFileChanges(reload, _documents, NullLogger<WatchedFileChanges>.Instance);

        await changes.ChangedAsync(
            [
                new FileEvent { Uri = DocumentUri.FromFileSystemPath(Path.Combine(Root, ProjectFile.FileName)), Type = type },
                new FileEvent { Uri = DocumentUri.FromFileSystemPath(Path.Combine(Root, "elsewhere", ProjectFile.FileName)), Type = FileChangeType.Changed },
            ], Substitute.For<OmniSharp.Extensions.LanguageServer.Protocol.Server.ILanguageServerFacade>());

        await reload.Received(reloads ? 1 : 0).ReloadAsync(Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task TheWatchedFileChanges_ReadTheOtherFilesAgain()
    {
        var reload = Substitute.For<IWorkspaceReloadService>();
        await LoadAsync("A.bas");
        var changes = new WatchedFileChanges(reload, _documents, NullLogger<WatchedFileChanges>.Instance);

        await changes.ChangedAsync([new FileEvent { Uri = DocumentUri.FromFileSystemPath(Path.Combine(Root, "A.bas")), Type = FileChangeType.Changed }],
            Substitute.For<OmniSharp.Extensions.LanguageServer.Protocol.Server.ILanguageServerFacade>());

        await reload.DidNotReceiveWithAnyArgs().ReloadAsync(default);
        await reload.Received(1).ReloadFilesAsync(Arg.Is<IReadOnlyList<Uri>>(files => files.Single() == UriOf("A.bas")), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task AModuleChangedOutsideTheEditor_IsReadAgain_AndTheWorkspaceIsDefinedAgain()
    {
        await LoadAsync("A.bas", "B.cls");
        _files.AddFile(Path.Combine(Root, "A.bas"), new MockFileData("Attribute VB_Name = \"A\"\r\nPublic Sub Changed()\r\nEnd Sub\r\n"));

        Assert.IsTrue(_documents.TryGetDocument(UriOf("A.bas"), out var before));
        var result = await _sut.ReloadFilesAsync([UriOf("A.bas")], CancellationToken.None);

        Assert.AreEqual(1, result?.Reread);
        Assert.IsTrue(_documents.TryGetDocument(UriOf("A.bas"), out var document));
        StringAssert.Contains(document.Text, "Changed");
        // the next version: the parse of the text before is not taken for the parse of this one.
        Assert.IsGreaterThan(before.Version, document.Version);
        await _parsing.Received(1).ParseDocumentAsync(UriOf("A.bas"), Arg.Any<CancellationToken>());
        await _symbols.Received(1).SyncWorkspaceAsync(Arg.Any<CancellationToken>(), null, true);
    }

    [TestMethod]
    public async Task AFileWhoseTextIsWhatWasLoaded_ChangesNothing()
    {
        await LoadAsync("A.bas");

        Assert.AreSame(WorkspaceReloadResult.Unchanged, await _sut.ReloadFilesAsync([UriOf("A.bas")], CancellationToken.None));
        await _symbols.DidNotReceiveWithAnyArgs().SyncWorkspaceAsync(default, default, default);
    }

    [TestMethod]
    public async Task AFileTheProjectDoesNotList_IsNothingOfTheWorkspace()
    {
        await LoadAsync("A.bas");
        _files.AddFile(Path.Combine(Root, "Elsewhere.bas"), new MockFileData("Attribute VB_Name = \"Elsewhere\"\r\n"));

        Assert.AreSame(WorkspaceReloadResult.Unchanged, await _sut.ReloadFilesAsync([UriOf("Elsewhere.bas"), UriOf("B.cls")], CancellationToken.None));
        Assert.IsFalse(_documents.TryGetDocument(UriOf("Elsewhere.bas"), out _));
    }

    [TestMethod]
    public async Task ADocumentTheClientHasOpen_IsTheClients_AndIsNotReadAgain()
    {
        await LoadAsync("A.bas");
        _documents.Open(UriOf("A.bas"), "Attribute VB_Name = \"A\"\r\n' the client's text\r\n", 1);
        _files.AddFile(Path.Combine(Root, "A.bas"), new MockFileData("Attribute VB_Name = \"A\"\r\n' the disk's text\r\n"));

        Assert.AreSame(WorkspaceReloadResult.Unchanged, await _sut.ReloadFilesAsync([UriOf("A.bas")], CancellationToken.None));
        Assert.IsTrue(_documents.TryGetDocument(UriOf("A.bas"), out var document));
        StringAssert.Contains(document.Text, "the client's text");
    }

    [TestMethod]
    public async Task AModuleWhoseFileIsGone_IsTakenOutOfTheHost_AndItsDocumentIsMissing()
    {
        await LoadAsync("A.bas", "B.cls");
        _files.RemoveFile(Path.Combine(Root, "B.cls"));

        var result = await _sut.ReloadFilesAsync([UriOf("B.cls")], CancellationToken.None);

        Assert.AreEqual(1, result?.Reread);
        Assert.IsInstanceOfType<MissingDocumentState>(_documentStates.GetCurrentState(new TextDocumentIdentifier(UriOf("B.cls"))));
        await _symbols.Received(1).DiscardModuleAsync("B", Arg.Any<CancellationToken>(), false);
        await _symbols.Received(1).SyncWorkspaceAsync(Arg.Any<CancellationToken>(), null, true);
    }
}
