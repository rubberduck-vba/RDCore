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

    // whether the text of the module, as it is now, has a parse: the parser gives none when it can't answer.
    private bool _parsed = true;

    // the documents of the workspace: what the project lists, on disk or not, and what another project lists.
    private readonly List<WorkspaceDocument> _listed = [];

    public PlatformDebugWorkspaceTests()
    {
        Directory.CreateDirectory(_root);
        _file = Path.Combine(_root, "Prog.bas");
        File.WriteAllText(_file, "Attribute VB_Name = \"Prog\"\r\nPublic Sub Main()\r\nEnd Sub\r\n");

        _listed.Add(new WorkspaceDocument("Prog.bas", _root, File.ReadAllText(_file)));
        _documents.GetAllDocuments().Returns(_ => [.. _listed]);
        _documents.WorkspaceRoot.Returns(_root);
        _parsing.TryGetCached(Arg.Any<Uri>(), out Arg.Any<ModuleParseResult>()).Returns(call =>
        {
            call[1] = Parse((Uri)call[0]);
            return true;
        });
        // a parse is of the version of the document it was made of: the text a load gave the document is parsed as the load makes it.
        _parsing.TryGetCached(Arg.Any<Uri>(), Arg.Any<int>(), out Arg.Any<ModuleParseResult>()).Returns(call =>
        {
            call[2] = Parse((Uri)call[0]);
            return _parsed && _listed.Any(document => document.Id.Uri.ToUri() == (Uri)call[0] && document.Version == (int)call[1]);
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

    private ModuleParseResult Parse(Uri uri) => new ModuleParser().Parse(uri, $"Attribute VB_Name = \"{_declaredName}\"\r\nPublic Sub Main()\r\nEnd Sub\r\n");

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

    // an edit saved and loaded is a new version of the document, which is what is parsed: the program goes on with it.
    [TestMethod]
    public async Task AnEditSavedAndLoaded_IsGoneOnWith()
    {
        var workspace = await OpenedAsync();
        _reload.ReloadFilesAsync(Arg.Any<IReadOnlyList<Uri>>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _listed[0] = _listed[0].WithText(_listed[0].Text + "' saved\r\n");
            return new WorkspaceReloadResult(0, 0, false, false, Reread: 1);
        });

        Assert.IsNull(await workspace.ReloadChangedAsync(CancellationToken.None));
    }

    // ---- what was so as the program started is no edit ----

    // a module the project lists that was not there as the program started is one the program ran without: nothing changed, and it goes on - and the file is
    // not read. A module of another project is no code of the program.
    [TestMethod]
    [DataRow(true, DisplayName = "listed by the project, missing")]
    [DataRow(false, DisplayName = "of another project, missing")]
    public async Task AFileMissingAsTheProgramStarted_IsNoReasonItCantGoOn(bool listedByTheProject)
    {
        var other = Path.Combine(_root, "other");
        _listed.Add(listedByTheProject ? new WorkspaceDocument("Old.bas", _root) : new WorkspaceDocument("Old.bas", other));
        var workspace = await OpenedAsync();
        _ = await workspace.StartAsync("Prog", "Main", CancellationToken.None);

        var refused = await workspace.ReloadChangedAsync(CancellationToken.None);

        Assert.IsNull(refused);
        await _reload.Received(1).ReloadFilesAsync(Arg.Is<IReadOnlyList<Uri>>(files => files.Single().LocalPath == _file), Arg.Any<CancellationToken>());
    }

    // a module the host could not load as the program started is one the program ran without: it goes on, and nothing is loaded again for it.
    [TestMethod]
    public async Task AModuleTheHostCouldNotLoadAsTheProgramStarted_IsNoReasonItCantGoOn()
    {
        _symbols.Unsynced.Returns(new Dictionary<string, string> { ["Prog"] = "the host refused it" });
        var workspace = await OpenedAsync();
        _ = await workspace.StartAsync("Prog", "Main", CancellationToken.None);

        var refused = await workspace.ReloadChangedAsync(CancellationToken.None);

        Assert.IsNull(refused);
        await _symbols.DidNotReceive().SyncWorkspaceAsync(Arg.Any<CancellationToken>(), null, true);
    }

    // the request that loaded a module's new name was refused for something else: the next one, with nothing saved since, is still refused for the name.
    [TestMethod]
    public async Task AModuleRenamed_ByARequestRefusedForSomethingElse_IsStillWhyTheProgramCantGoOn()
    {
        var workspace = await OpenedAsync();
        _declaredName = "Renamed";
        _reload.ReloadFilesAsync(Arg.Any<IReadOnlyList<Uri>>(), Arg.Any<CancellationToken>()).Returns(new WorkspaceReloadResult(0, 0, false, false, Reread: 1));
        _symbols.Unsynced.Returns(new Dictionary<string, string> { ["Renamed"] = "the host refused it" });

        var refused = await workspace.ReloadChangedAsync(CancellationToken.None);
        _reload.ReloadFilesAsync(Arg.Any<IReadOnlyList<Uri>>(), Arg.Any<CancellationToken>()).Returns(WorkspaceReloadResult.Unchanged);
        _symbols.Unsynced.Returns(new Dictionary<string, string>());
        var refusedAgain = await workspace.ReloadChangedAsync(CancellationToken.None);

        StringAssert.Contains(refused, "the host refused it");
        StringAssert.Contains(refusedAgain, "Prog");
        StringAssert.Contains(refusedAgain, "Renamed");
    }

    // a module named back as it was is the module the program has.
    [TestMethod]
    public async Task AModuleNamedBack_IsNoReasonItCantGoOn()
    {
        var workspace = await OpenedAsync();
        _declaredName = "Renamed";
        _reload.ReloadFilesAsync(Arg.Any<IReadOnlyList<Uri>>(), Arg.Any<CancellationToken>()).Returns(new WorkspaceReloadResult(0, 0, false, false, Reread: 1));
        var refused = await workspace.ReloadChangedAsync(CancellationToken.None);

        _declaredName = "Prog";
        var namedBack = await workspace.ReloadChangedAsync(CancellationToken.None);

        Assert.IsNotNull(refused);
        Assert.IsNull(namedBack);
    }

    // the parser gave no parse of the text as it is now: the program does not have it, which it is told until it is parsed - asked again by the next request.
    [TestMethod]
    public async Task AModuleTheParserGaveNoParseOf_IsWhyTheProgramCantGoOn_UntilItIsParsed()
    {
        var workspace = await OpenedAsync();
        _parsed = false;

        var refused = await workspace.ReloadChangedAsync(CancellationToken.None);
        var refusedAgain = await workspace.ReloadChangedAsync(CancellationToken.None);
        _parsed = true;
        var parsed = await workspace.ReloadChangedAsync(CancellationToken.None);

        StringAssert.Contains(refused, "Prog.bas");
        Assert.AreEqual(refused, refusedAgain, "nothing saved since: still not parsed");
        Assert.IsNull(parsed);
        await _parsing.Received(2).ParseDocumentAsync(Arg.Is<Uri>(uri => uri.LocalPath == _file), Arg.Any<CancellationToken>());
    }

    // a load that failed left the host with what nothing says: the next request loads the workspace again before the program goes on.
    [TestMethod]
    public async Task ALoadThatFailed_IsDoneAgain_ByTheNextRequest()
    {
        var workspace = await OpenedAsync();
        _reload.ReloadFilesAsync(Arg.Any<IReadOnlyList<Uri>>(), Arg.Any<CancellationToken>()).Returns(Task.FromException<WorkspaceReloadResult?>(new IOException("the parser is gone")));
        _ = await Assert.ThrowsAsync<IOException>(() => workspace.ReloadChangedAsync(CancellationToken.None));
        _reload.ReloadFilesAsync(Arg.Any<IReadOnlyList<Uri>>(), Arg.Any<CancellationToken>()).Returns(WorkspaceReloadResult.Unchanged);
        _symbols.ClearReceivedCalls();

        var goneOn = await workspace.ReloadChangedAsync(CancellationToken.None);
        var again = await workspace.ReloadChangedAsync(CancellationToken.None);

        Assert.IsNull(goneOn);
        Assert.IsNull(again);
        await _symbols.Received(1).SyncWorkspaceAsync(Arg.Any<CancellationToken>(), null, true);
    }
}
