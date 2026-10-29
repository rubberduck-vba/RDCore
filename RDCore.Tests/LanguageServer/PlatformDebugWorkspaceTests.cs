using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using RDCore.LanguageServer;
using RDCore.LanguageServer.Debugging;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.Parsing;
using RDCore.SDK.Client;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.Services.States;
using RDCore.SDK.Workspace;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// What a debug adapter's workspace says of what is saved, as the program that waits is asked to go on: a file that can't be read, a module the host could not
/// load, and a module named otherwise by its source are each why the program can't, on the files of a folder as they are on disk.
/// </summary>
[TestClass]
public sealed class PlatformDebugWorkspaceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rdcore-debug-workspace", Guid.NewGuid().ToString("N"));
    private readonly string _file;
    private readonly IWorkspaceDocumentService _documents = Substitute.For<IWorkspaceDocumentService>();
    private readonly IParsingClientService _parsing = Substitute.For<IParsingClientService>();
    private readonly ISymbolSyncService _symbols = Substitute.For<ISymbolSyncService>();
    private readonly IWorkspaceReloadService _reload = Substitute.For<IWorkspaceReloadService>();
    private readonly IPlatformOrchestrationService _orchestration = Substitute.For<IPlatformOrchestrationService>();
    private readonly IRDCoreClientApp _host = Substitute.For<IRDCoreClientApp>();

    // the name the module's source gives it, as it is parsed now.
    private string _declaredName = "Prog";

    public PlatformDebugWorkspaceTests()
    {
        Directory.CreateDirectory(_root);
        _file = Path.Combine(_root, "Prog.bas");
        File.WriteAllText(_file, "Attribute VB_Name = \"Prog\"\r\nPublic Sub Main()\r\nEnd Sub\r\n");

        var document = new WorkspaceDocument("Prog.bas", _root, File.ReadAllText(_file));
        _documents.GetAllDocuments().Returns(_ => [document]);
        _documents.WorkspaceRoot.Returns(_root);
        _parsing.TryGetCached(Arg.Any<Uri>(), out Arg.Any<ModuleParseResult>()).Returns(call =>
        {
            call[1] = new ModuleParser().Parse((Uri)call[0], $"Attribute VB_Name = \"{_declaredName}\"\r\nPublic Sub Main()\r\nEnd Sub\r\n");
            return true;
        });
        _symbols.Unsynced.Returns(new Dictionary<string, string>());
        _reload.ReloadFilesAsync(Arg.Any<IReadOnlyList<Uri>>(), Arg.Any<CancellationToken>()).Returns(WorkspaceReloadResult.Unchanged);
        _reload.WhenIdleAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        // the components are up, and are not lost.
        foreach (var component in new[] { _host, Substitute.For<IRDCoreClientApp>() })
        {
            component.WaitForTerminalAsync().Returns(new TaskCompletionSource().Task);
        }

        _orchestration.RuntimeEnvironment.Returns(_host);
        _host.SendRequestAsync<HostExecuteParams, ExecuteSessionResult>(Arg.Any<HostExecuteParams>(), Arg.Any<CancellationToken>())
            .Returns(new ExecuteSessionResult { Outcome = ExecutionOutcome.Suspended });
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // what a test left open is the system's to clean.
        }
    }

    private async Task<PlatformDebugWorkspace> OpenedAsync()
    {
        var options = Options.Create(new SdkAppOptions { Workspace = new SdkWorkspaceOptions { WorkspaceUri = _root } });
        var workspace = new PlatformDebugWorkspace(Substitute.For<IServiceProvider>(), Substitute.For<IServerStateProvider>(), _orchestration,
            Substitute.For<IWorkspaceService>(), _documents, _parsing, _symbols, _reload, new System.IO.Abstractions.FileSystem(), options,
            NullLogger<PlatformDebugWorkspace>.Instance);
        await workspace.OpenAsync(CancellationToken.None);
        return workspace;
    }

    [TestMethod]
    public async Task WhatIsSaved_AsItWas_IsGoneOnWith()
        => Assert.IsNull(await (await OpenedAsync()).ReloadChangedAsync(CancellationToken.None));

    // what is saved is read before the program is checked against it, and what another request is loading still is waited for.
    [TestMethod]
    public async Task WhatIsSaved_IsReadAndWaitedFor_BeforeTheProgramIsChecked()
    {
        var workspace = await OpenedAsync();

        _ = await workspace.ReloadChangedAsync(CancellationToken.None);

        Received.InOrder(() =>
        {
            _reload.ReloadFilesAsync(Arg.Is<IReadOnlyList<Uri>>(files => files.Single().LocalPath == _file), Arg.Any<CancellationToken>());
            _reload.WhenIdleAsync(Arg.Any<CancellationToken>());
        });
    }

    [TestMethod]
    public async Task AFileDeletedWhileTheProgramWaits_IsWhyItCantGoOn()
    {
        var workspace = await OpenedAsync();
        File.Delete(_file);

        var refused = await workspace.ReloadChangedAsync(CancellationToken.None);

        StringAssert.Contains(refused, "Prog.bas");
        await _reload.DidNotReceiveWithAnyArgs().ReloadFilesAsync(default!, default);
    }

    // a file another program holds open, and shares with no one, can't be read; only Windows locks a file so.
    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public async Task AFileHeldOpenWhileTheProgramWaits_IsWhyItCantGoOn()
    {
        var workspace = await OpenedAsync();
        using var held = new FileStream(_file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var refused = await workspace.ReloadChangedAsync(CancellationToken.None);

        StringAssert.Contains(refused, "Prog.bas");
    }

    // the host has the module as it was: the program can't go on with its code, and the next request loads it again, whatever else is saved.
    [TestMethod]
    public async Task AModuleTheHostCouldNotLoad_IsWhyTheProgramCantGoOn_AndIsLoadedAgainByTheNextRequest()
    {
        var workspace = await OpenedAsync();
        _symbols.Unsynced.Returns(new Dictionary<string, string> { ["Prog"] = "the host refused it" });
        _symbols.ClearReceivedCalls();

        var refused = await workspace.ReloadChangedAsync(CancellationToken.None);

        StringAssert.Contains(refused, "Prog");
        StringAssert.Contains(refused, "the host refused it");
        await _symbols.Received(1).SyncWorkspaceAsync(Arg.Any<CancellationToken>(), null, true);

        // loaded, this time.
        _symbols.Unsynced.Returns(new Dictionary<string, string>());
        Assert.IsNull(await workspace.ReloadChangedAsync(CancellationToken.None));
    }

    // a module named otherwise by its source is a module the program does not have, until it is run again.
    [TestMethod]
    public async Task AModuleRenamedByItsSource_IsWhyTheProgramCantGoOn_UntilItIsRunAgain()
    {
        var workspace = await OpenedAsync();
        _declaredName = "Renamed";
        _reload.ReloadFilesAsync(Arg.Any<IReadOnlyList<Uri>>(), Arg.Any<CancellationToken>()).Returns(new WorkspaceReloadResult(0, 0, false, false, Reread: 1));

        var refused = await workspace.ReloadChangedAsync(CancellationToken.None);
        _reload.ReloadFilesAsync(Arg.Any<IReadOnlyList<Uri>>(), Arg.Any<CancellationToken>()).Returns(WorkspaceReloadResult.Unchanged);
        var refusedAgain = await workspace.ReloadChangedAsync(CancellationToken.None);
        _ = await workspace.StartAsync("Renamed", "Main", CancellationToken.None);
        var runAgain = await workspace.ReloadChangedAsync(CancellationToken.None);

        StringAssert.Contains(refused, "Prog");
        StringAssert.Contains(refused, "Renamed");
        Assert.AreEqual(refused, refusedAgain, "nothing saved since: the program still does not have it");
        Assert.IsNull(runAgain, "run again, the program has the module as it is named now");
    }
}
