using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
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
    private readonly IWorkspaceRefresh _refresh = Substitute.For<IWorkspaceRefresh>();
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
        _sut = new WorkspaceReloadService(_state, _projectFiles, _documents, _documentStates, _parsing, _symbols, orchestration, _files.Path, _refresh,
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
        _refresh.Received(1).Changed();
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
        _refresh.DidNotReceive().Changed();
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
        _refresh.Received(1).Changed();
    }

    // a project file that cannot be written leaves the project what it was - what is on disk, and what the workspace is - and says it was not written.
    [TestMethod]
    public async Task AChangeThatCannotBeWritten_LeavesTheProjectAsItWas()
    {
        await LoadAsync("A.bas");
        _files.File.SetAttributes(Path.Combine(Root, ProjectFile.FileName), FileAttributes.ReadOnly);

        var notWritten = await Assert.ThrowsAsync<ProjectFileNotWrittenException>(
            () => _sut.ChangeAsync(project => project.WithModule(new RDCoreModule { RelativeUri = "B.cls" }), CancellationToken.None));

        Assert.IsInstanceOfType<UnauthorizedAccessException>(notWritten.InnerException);

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
        _refresh.DidNotReceive().Changed();
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

    // what a component logs, kept.
    private sealed class CapturingLogger : ILogger<WorkspaceReloadService>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Entries)
            {
                Entries.Add((logLevel, formatter(state, exception), exception));
            }
        }
    }

    private WorkspaceReloadService ServiceLogging(CapturingLogger log)
    {
        var orchestration = Substitute.For<IPlatformOrchestrationService>();
        orchestration.RuntimeEnvironment.Returns(_host);
        return new WorkspaceReloadService(_state, _projectFiles, _documents, _documentStates, _parsing, _symbols, orchestration, _files.Path, _refresh, log);
    }

    // set when the environment host is first waited for: the work that waits for it has come as far as it can.
    private readonly TaskCompletionSource _hostWaitedFor = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // the environment host is ready when the test says so.
    private TaskCompletionSource HostGate()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _host.WaitForReadyAsync(Arg.Any<CancellationToken>()).Returns(call =>
        {
            _hostWaitedFor.TrySetResult();
            return gate.Task.WaitAsync(call.Arg<CancellationToken>());
        });
        return gate;
    }

    private static Task<WorkspaceReloadStep?> Removing(WorkspaceReloadService sut, int index)
        => sut.BeginChangeAsync(project => project.WithoutModule(project.ProjectInfo.Modules[index]), CancellationToken.None);

    private static Task<WorkspaceReloadStep?> Adding(WorkspaceReloadService sut, string module, List<string> order)
        => sut.BeginChangeAsync(project =>
        {
            order.Add("+" + module);
            return project.WithModule(new RDCoreModule { RelativeUri = module });
        }, CancellationToken.None);

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task TwoChanges_WhileTheHostIsNotReady_AreBothMade_AndDoneTogether_WhenItIs()
    {
        await LoadAsync("A.bas");
        _files.AddFile(Path.Combine(Root, "C.bas"), new MockFileData("Attribute VB_Name = \"C\"\r\n"));
        _files.AddFile(Path.Combine(Root, "D.bas"), new MockFileData("Attribute VB_Name = \"D\"\r\n"));
        var gate = HostGate();
        var order = new List<string>();

        // the first change gains two modules, and the work for it is held at the host before the second change is made.
        var first = (await _sut.BeginChangeAsync(project => project.WithModule(new RDCoreModule { RelativeUri = "B.cls" }).WithModule(new RDCoreModule { RelativeUri = "D.bas" }), CancellationToken.None))!;
        await _hostWaitedFor.Task.WaitAsync(TimeSpan.FromSeconds(20));
        var second = (await Adding(_sut, "C.bas", order))!;

        // the second change did not wait for the first one's host: both are in the workspace, and neither is done.
        Assert.IsTrue(_documents.TryGetDocument(UriOf("B.cls"), out _));
        Assert.IsTrue(_documents.TryGetDocument(UriOf("C.bas"), out _));
        Assert.IsFalse(first.Completed.IsCompleted);
        Assert.IsFalse(second.Completed.IsCompleted);

        gate.SetResult();
        Assert.AreEqual(new WorkspaceReloadResult(Added: 2, Removed: 0, IsHostComposed: true, IsProgramRunning: false), await first.Completed.WaitAsync(TimeSpan.FromSeconds(20)), "each change has its own result");
        Assert.AreEqual(new WorkspaceReloadResult(Added: 1, Removed: 0, IsHostComposed: true, IsProgramRunning: false), await second.Completed.WaitAsync(TimeSpan.FromSeconds(20)));

        // the host composes its session again, and the symbols are defined again, once for the changes that waited together.
        await _host.Received(1).SendRequestAsync<HostProjectReloadParams, HostProjectReloadResult>(Arg.Any<HostProjectReloadParams>(), Arg.Any<CancellationToken>());
        await _symbols.Received(1).SyncWorkspaceAsync(Arg.Any<CancellationToken>(), null, true);
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task ChangesMadeAtTheSameTime_AreDoneInTheOrderTheyWereMade()
    {
        var names = Enumerable.Range(0, 24).Select(index => $"M{index}.bas").ToArray();
        await LoadAsync("A.bas");
        foreach (var name in names)
        {
            _files.AddFile(Path.Combine(Root, name), new MockFileData($"Attribute VB_Name = \"{Path.GetFileNameWithoutExtension(name)}\"\r\n"));
        }

        // every change is made on a thread of its own, and the file system here answers at once: the lock of the changes is passed from one to the next with nothing between.
        var made = new List<string>();
        var steps = await Task.WhenAll(names.Select(name => Task.Run(async () => await _sut.BeginChangeAsync(project =>
        {
            lock (made)
            {
                made.Add(name);
            }

            return project.WithModule(new RDCoreModule { RelativeUri = name });
        }, CancellationToken.None))));
        await Task.WhenAll(steps.Select(step => step!.Completed)).WaitAsync(TimeSpan.FromSeconds(30));

        // each change's module is parsed once its work is done: the order of the parses is the order the work was done in.
        var parsed = _parsing.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IParsingClientService.ParseDocumentAsync))
            .Select(call => Path.GetFileName(((Uri)call.GetArguments()[0]!).LocalPath))
            .ToArray();
        CollectionAssert.AreEqual(made.ToArray(), parsed, "the changes are done in the order they were made");
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task ADocumentTheClientEdits_WhileAChangeWaitsForTheHost_KeepsItsText_WhenTheHostIsReady()
    {
        await LoadAsync("A.bas");
        var gate = HostGate();
        var step = (await Adding(_sut, "B.cls", []))!;
        _documents.Open(UriOf("B.cls"), "Attribute VB_Name = \"B\"\r\n'typed", 5);

        gate.SetResult();
        await step.Completed.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.IsTrue(_documents.TryGetDocument(UriOf("B.cls"), out var document));
        Assert.AreEqual("Attribute VB_Name = \"B\"\r\n'typed", document.Text, "what the client typed is the document, after the work that waited for the host was done");
        Assert.AreEqual(5, document.Version);
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task WhenTheServerStops_WhatWaitsForTheHost_IsCancelled_AndNothingIsDefined()
    {
        await LoadAsync("A.bas");
        HostGate();
        var order = new List<string>();
        var first = (await Adding(_sut, "B.cls", order))!;
        var second = (await _sut.BeginChangeAsync(project => project.WithoutModule(project.ProjectInfo.Modules[0]), CancellationToken.None))!;

        _sut.Stop();

        await Assert.ThrowsAsync<OperationCanceledException>(() => first.Completed.WaitAsync(TimeSpan.FromSeconds(20)));
        await Assert.ThrowsAsync<OperationCanceledException>(() => second.Completed.WaitAsync(TimeSpan.FromSeconds(20)));
        await _symbols.DidNotReceiveWithAnyArgs().SyncWorkspaceAsync(default, default, default);
        _refresh.DidNotReceive().Changed();
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task AChangeMadeAfterTheServerStopped_IsNotDefined_WhateverItHasToDo()
    {
        // no environment host at all: with nothing to wait for, the work that was left would otherwise be done.
        var orchestration = Substitute.For<IPlatformOrchestrationService>();
        var sut = new WorkspaceReloadService(_state, _projectFiles, _documents, _documentStates, _parsing, _symbols, orchestration, _files.Path, _refresh, NullLogger<WorkspaceReloadService>.Instance);
        await LoadAsync("A.bas");
        sut.Stop();

        var step = (await Adding(sut, "B.cls", []))!;

        Assert.IsTrue(_documents.TryGetDocument(UriOf("B.cls"), out _), "the documents are what the project lists");
        await Assert.ThrowsAsync<OperationCanceledException>(() => step.Completed.WaitAsync(TimeSpan.FromSeconds(20)));
        await _symbols.DidNotReceiveWithAnyArgs().SyncWorkspaceAsync(default, default, default);
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task WhatIsMadeWhileABatchIsWorking_IsTheNextBatch()
    {
        await LoadAsync("A.bas");
        _files.AddFile(Path.Combine(Root, "C.bas"), new MockFileData("Attribute VB_Name = \"C\"\r\n"));
        var syncing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var syncs = 0;
        _symbols.SyncWorkspaceAsync(default, default, default).ReturnsForAnyArgs(_ =>
        {
            if (Interlocked.Increment(ref syncs) == 1)
            {
                syncing.SetResult();
                return release.Task;
            }

            return Task.CompletedTask;
        });

        var first = (await Adding(_sut, "B.cls", []))!;
        await syncing.Task.WaitAsync(TimeSpan.FromSeconds(20));
        var second = (await Adding(_sut, "C.bas", []))!;
        release.SetResult();

        await first.Completed.WaitAsync(TimeSpan.FromSeconds(20));
        await second.Completed.WaitAsync(TimeSpan.FromSeconds(20));
        await _host.Received(2).SendRequestAsync<HostProjectReloadParams, HostProjectReloadResult>(Arg.Any<HostProjectReloadParams>(), Arg.Any<CancellationToken>());
        Assert.AreEqual(2, syncs, "the second change was not done with the batch that was already defining the workspace");
    }

    // what a program that waits is checked against is the workspace once no change is being made: a batch defining it, and one queued meanwhile, are waited for.
    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task Idle_IsOnceTheBatchBeingDone_AndTheOneMadeMeanwhile_AreDone()
    {
        await LoadAsync("A.bas");
        _files.AddFile(Path.Combine(Root, "C.bas"), new MockFileData("Attribute VB_Name = \"C\"\r\n"));
        var syncing = new[] { new TaskCompletionSource(), new TaskCompletionSource() };
        var release = new[] { new TaskCompletionSource(), new TaskCompletionSource() };
        var syncs = 0;
        _symbols.SyncWorkspaceAsync(default, default, default).ReturnsForAnyArgs(async _ =>
        {
            var call = Interlocked.Increment(ref syncs) - 1;
            syncing[call].TrySetResult();
            await release[call].Task;
        });

        _ = (await Adding(_sut, "B.cls", []))!;
        await syncing[0].Task.WaitAsync(TimeSpan.FromSeconds(20));
        var idle = _sut.WhenIdleAsync(CancellationToken.None);
        _ = (await Adding(_sut, "C.bas", []))!;
        release[0].SetResult();
        await syncing[1].Task.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.IsFalse(idle.IsCompleted, "the change made meanwhile is being done");
        release[1].SetResult();
        await idle.WaitAsync(TimeSpan.FromSeconds(20));
    }

    [TestMethod]
    public async Task Idle_IsAtOnce_WhenNoChangeIsBeingMade()
    {
        await LoadAsync("A.bas");

        await _sut.WhenIdleAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(20));
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task AModuleTakenOutAndPutBackInOneBatch_IsInTheWorkspace_AndTheHostIsNotToldItIsGone_WhenItComposedAgain()
    {
        await LoadAsync("A.bas", "B.cls");
        var gate = HostGate();

        var removed = (await Removing(_sut, 1))!;
        await _hostWaitedFor.Task.WaitAsync(TimeSpan.FromSeconds(20));
        var added = (await Adding(_sut, "B.cls", []))!;
        gate.SetResult();

        Assert.AreEqual(new WorkspaceReloadResult(Added: 0, Removed: 1, IsHostComposed: true, IsProgramRunning: false), await removed.Completed.WaitAsync(TimeSpan.FromSeconds(20)));
        Assert.AreEqual(new WorkspaceReloadResult(Added: 1, Removed: 0, IsHostComposed: true, IsProgramRunning: false), await added.Completed.WaitAsync(TimeSpan.FromSeconds(20)));
        Assert.IsTrue(_documents.TryGetDocument(UriOf("B.cls"), out _));
        await _symbols.DidNotReceiveWithAnyArgs().DiscardModuleAsync(default!, default);
        await _symbols.Received(1).SyncWorkspaceAsync(Arg.Any<CancellationToken>(), null, true);
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task AModuleTakenOut_IsForgottenByAHostThatKeptItsSession_AndTheWorkspaceIsDefinedAfterwards()
    {
        await LoadAsync("A.bas", "B.cls");
        HostAnswers(new HostProjectReloadResult { Composed = false, IsProgramRunning = true });

        var removed = (await Removing(_sut, 1))!;
        await removed.Completed.WaitAsync(TimeSpan.FromSeconds(20));

        await _symbols.Received(1).DiscardModuleAsync("B", Arg.Any<CancellationToken>());
        await _symbols.Received(1).SyncWorkspaceAsync(Arg.Any<CancellationToken>(), null, true);
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task AModuleThatCannotBeParsed_IsTheFailureOfItsChangeOnly_AndTheWorkspaceIsDefinedAllTheSame()
    {
        await LoadAsync("A.bas");
        _files.AddFile(Path.Combine(Root, "C.bas"), new MockFileData("Attribute VB_Name = \"C\"\r\n"));
        var log = new CapturingLogger();
        var sut = ServiceLogging(log);
        _parsing.ParseDocumentAsync(Arg.Is<Uri>(uri => uri.LocalPath.EndsWith("B.cls", StringComparison.Ordinal)), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("the parse server is gone"));
        var gate = HostGate();

        var broken = (await Adding(sut, "B.cls", []))!;
        await _hostWaitedFor.Task.WaitAsync(TimeSpan.FromSeconds(20));
        var fine = (await Adding(sut, "C.bas", []))!;
        gate.SetResult();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => broken.Completed.WaitAsync(TimeSpan.FromSeconds(20)));
        Assert.AreEqual("the parse server is gone", failure.Message);
        Assert.AreEqual(new WorkspaceReloadResult(Added: 1, Removed: 0, IsHostComposed: true, IsProgramRunning: false), await fine.Completed.WaitAsync(TimeSpan.FromSeconds(20)));
        await _host.Received(1).SendRequestAsync<HostProjectReloadParams, HostProjectReloadResult>(Arg.Any<HostProjectReloadParams>(), Arg.Any<CancellationToken>());
        await _symbols.Received(1).SyncWorkspaceAsync(Arg.Any<CancellationToken>(), null, true);
        lock (log.Entries)
        {
            var line = log.Entries.Single(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("could not be parsed", StringComparison.Ordinal));
            StringAssert.Contains(line.Message, "B.cls");
            Assert.AreSame(failure, line.Exception);
        }
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task ARequestThatWasCancelled_ButNotByTheServerStopping_IsAFailure_AndIsLogged()
    {
        await LoadAsync("A.bas");
        var log = new CapturingLogger();
        var sut = ServiceLogging(log);
        _host.SendRequestAsync<HostProjectReloadParams, HostProjectReloadResult>(Arg.Any<HostProjectReloadParams>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException("the connection to the host was lost"));

        var step = (await Adding(sut, "B.cls", []))!;
        var failed = await Assert.ThrowsAsync<OperationCanceledException>(() => step.Completed.WaitAsync(TimeSpan.FromSeconds(20)));

        Assert.AreEqual("the connection to the host was lost", failed.Message);
        Assert.IsTrue(step.Completed.IsFaulted, "it failed");
        Assert.IsFalse(step.Completed.IsCanceled, "it was not cancelled: only the server stopping does that");
        lock (log.Entries)
        {
            Assert.AreEqual(1, log.Entries.Count(entry => entry.Level == LogLevel.Error && ReferenceEquals(entry.Exception, failed)));
        }

        await _symbols.DidNotReceiveWithAnyArgs().SyncWorkspaceAsync(default, default, default);
        _refresh.Received(1).Changed();
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task ABatchWhoseHostWasLost_StillChangedTheWorkspace_SoTheClientIsToldOnce()
    {
        await LoadAsync("A.bas");
        _files.AddFile(Path.Combine(Root, "C.bas"), new MockFileData("Attribute VB_Name = \"C\"\r\n"));
        var gate = HostGate();
        _host.SendRequestAsync<HostProjectReloadParams, HostProjectReloadResult>(Arg.Any<HostProjectReloadParams>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("the connection to the host was lost"));

        var first = (await Adding(_sut, "B.cls", []))!;
        await _hostWaitedFor.Task.WaitAsync(TimeSpan.FromSeconds(20));
        var second = (await Adding(_sut, "C.bas", []))!;
        gate.SetResult();

        await Assert.ThrowsAsync<IOException>(() => first.Completed.WaitAsync(TimeSpan.FromSeconds(20)));
        await Assert.ThrowsAsync<IOException>(() => second.Completed.WaitAsync(TimeSpan.FromSeconds(20)));

        // the documents are made, and the generation moved on once for the batch: a pull is not answered unchanged.
        Assert.IsTrue(_documents.TryGetDocument(UriOf("B.cls"), out _));
        _refresh.Received(1).Changed();
    }

    [TestMethod]
    public async Task TheEndOfTheWork_IsLogged_WhenItFailedBeingCancelled_AndNotWhenTheServerStopped()
    {
        var server = Substitute.For<OmniSharp.Extensions.LanguageServer.Protocol.Server.ILanguageServerFacade>();
        var log = new CapturingLogger();

        var stopped = new TaskCompletionSource<WorkspaceReloadResult>();
        stopped.SetCanceled();
        await WorkspaceFileOperationFilters.ReportCompletedAsync(stopped.Task, server, log);

        var lost = new TaskCompletionSource<WorkspaceReloadResult>();
        lost.SetException(new OperationCanceledException("the connection to the host was lost"));
        await WorkspaceFileOperationFilters.ReportCompletedAsync(lost.Task, server, log);

        lock (log.Entries)
        {
            Assert.AreEqual(1, log.Entries.Count(entry => entry.Level == LogLevel.Error), "only the one that failed is logged");
        }
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task TheWatchedFileChanges_ReadTheFilesWithoutWaitingForTheHost_AndTellTheUserWhenTheWorkIsDone()
    {
        var reload = Substitute.For<IWorkspaceReloadService>();
        var done = new TaskCompletionSource<WorkspaceReloadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new WorkspaceReloadResult(0, 0, false, false, Reread: 1);
        reload.BeginReloadFilesAsync(Arg.Any<IReadOnlyList<Uri>>(), Arg.Any<CancellationToken>()).Returns(new WorkspaceReloadStep(started, done.Task));
        await LoadAsync("A.bas");
        var changes = new WatchedFileChanges(reload, _documents, NullLogger<WatchedFileChanges>.Instance);
        var server = Substitute.For<OmniSharp.Extensions.LanguageServer.Protocol.Server.ILanguageServerFacade>();

        // the host is not ready: the call returns, and the user has not been told that a program kept its session.
        await changes.ChangedAsync([new FileEvent { Uri = DocumentUri.FromFileSystemPath(Path.Combine(Root, "A.bas")), Type = FileChangeType.Changed }], server);
        Assert.IsFalse(server.Window.ReceivedCalls().Any());

        done.SetResult(new WorkspaceReloadResult(0, 0, false, true, Reread: 1));
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!server.Window.ReceivedCalls().Any() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.IsTrue(server.Window.ReceivedCalls().Any(), "the user is told once the work is done");
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

        await reload.Received(reloads ? 1 : 0).BeginReloadAsync(Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task TheWatchedFileChanges_ReadTheOtherFilesAgain()
    {
        var reload = Substitute.For<IWorkspaceReloadService>();
        await LoadAsync("A.bas");
        var changes = new WatchedFileChanges(reload, _documents, NullLogger<WatchedFileChanges>.Instance);

        await changes.ChangedAsync([new FileEvent { Uri = DocumentUri.FromFileSystemPath(Path.Combine(Root, "A.bas")), Type = FileChangeType.Changed }],
            Substitute.For<OmniSharp.Extensions.LanguageServer.Protocol.Server.ILanguageServerFacade>());

        await reload.DidNotReceiveWithAnyArgs().BeginReloadAsync(default);
        await reload.Received(1).BeginReloadFilesAsync(Arg.Is<IReadOnlyList<Uri>>(files => files.Single() == UriOf("A.bas")), Arg.Any<CancellationToken>());
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
        _refresh.Received(1).Changed();
    }

    [TestMethod]
    public async Task AFileWhoseTextIsWhatWasLoaded_ChangesNothing()
    {
        await LoadAsync("A.bas");

        Assert.AreSame(WorkspaceReloadResult.Unchanged, await _sut.ReloadFilesAsync([UriOf("A.bas")], CancellationToken.None));
        await _symbols.DidNotReceiveWithAnyArgs().SyncWorkspaceAsync(default, default, default);
        _refresh.DidNotReceive().Changed();
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
