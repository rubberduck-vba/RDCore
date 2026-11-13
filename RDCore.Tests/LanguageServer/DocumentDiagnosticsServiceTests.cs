using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.JsonRpc.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer;
using RDCore.LanguageServer.Debugging;
using RDCore.LanguageServer.Diagnostics;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.Parsing;
using RDCore.SDK.Client;
using RDCore.SDK.Extensibility;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server.Progress;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;
using RDCore.SDK.Platform.Components;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Tests.LanguageServer;

[TestClass]
public sealed class DocumentDiagnosticsServiceTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-diag-ws");

    private readonly IWorkspaceDocumentService _documents = Substitute.For<IWorkspaceDocumentService>();
    private readonly IParsingClientService _parsing = Substitute.For<IParsingClientService>();
    private readonly IPlatformOrchestrationService _orchestration = Substitute.For<IPlatformOrchestrationService>();
    private readonly ISymbolSyncService _symbols = Substitute.For<ISymbolSyncService>();
    private readonly WorkspaceRefresh _refresh = new(TimeSpan.Zero);

    private DocumentDiagnosticsService Sut(IEditStatusMonitor? editStatus = null)
        => new(_documents, _parsing, _orchestration, _symbols, _refresh, NullLogger<DocumentDiagnosticsService>.Instance, editStatus);

    private static WorkspaceDocument Document(int version = 1)
        => new("src/Mod1.bas", Root, "Public Sub Foo()\r\nEnd Sub", version);

    private void WorkspaceHas(params WorkspaceDocument[] docs)
    {
        _documents.GetAllDocuments().Returns(docs);
        foreach (var doc in docs)
        {
            _documents.TryGetDocument(doc.Id.Uri.ToUri(), out Arg.Any<WorkspaceDocument>())
                .Returns(call => { call[1] = doc; return true; });
        }
    }

    private void WorkspaceVersions(WorkspaceDocument first, WorkspaceDocument then)
    {
        _documents.GetAllDocuments().Returns([first], [then]);
        // both versions live at the same URI (same relative path/root); TryGetDocument must
        // resolve the version GetAllDocuments would have handed out for the same call.
        _documents.TryGetDocument(first.Id.Uri.ToUri(), out Arg.Any<WorkspaceDocument>())
            .Returns(
                call => { call[1] = first; return true; },
                call => { call[1] = then; return true; });
    }

    private void ParseYields()
        => _parsing.ParseDocumentAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>())
            .Returns(new ModuleParser().Parse(TestUri.TestModuleUri(), "Public Sub Foo()\r\nEnd Sub"));

    private static Diagnostic Diag(int line, string code = "VBC01027", string source = "RDCore", string message = "Syntax error")
        => new()
        {
            Code = new DiagnosticCode(code),
            Source = source,
            Message = message,
            Severity = DiagnosticSeverity.Error,
            Range = new Range(new Position(line, 0), new Position(line, 4)),
        };

    // the platform bundles RDCore.Diagnostics: its findings are the platform's own.
    private static ExtensionInfo Manifest(string title, string capability, string? prefix = null) => new()
    {
        Name = $"{title}.exe",
        Title = title,
        Version = new Version(1, 0),
        Publisher = "publisher",
        PublisherWebUrl = "https://example.test",
        Description = "desc",
        FileHash = "hash",
        Capabilities = [new PlatformExtensionServerCapability(capability)],
        DiagnosticPrefix = prefix ?? (title == "RDCore.Diagnostics" ? "RDC" : null),
        IsCoreExtension = title == "RDCore.Diagnostics",
    };

    private IRDCoreClientApp Provider(string title, int sourceVersion, params Diagnostic[] diagnostics)
        => Provider(Manifest(title, nameof(DiagnoseDocument)), sourceVersion, diagnostics);

    private IRDCoreClientApp Provider(ExtensionInfo manifest, int sourceVersion, params Diagnostic[] diagnostics)
    {
        var app = Substitute.For<IRDCoreClientApp>();
        app.ExtensionInfo.Returns(manifest);
        app.WaitForReadyAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        app.SendRequestAsync<DiagnoseDocumentParams, DiagnoseDocumentResult>(Arg.Any<DiagnoseDocumentParams>(), Arg.Any<CancellationToken>())
            .Returns(new DiagnoseDocumentResult { Diagnostics = diagnostics, SourceVersion = sourceVersion });
        return app;
    }

    private static IRDCoreClientApp NonProvider()
    {
        var app = Substitute.For<IRDCoreClientApp>();
        app.ExtensionInfo.Returns(Manifest("Other", "CliCommand"));
        return app;
    }

    private void ProvidersAre(params IRDCoreClientApp[] providers)
        => _orchestration.Extensions.Returns(providers);

    [TestMethod]
    public async Task NoProviders_ReturnsAFreshEmptyReport()
    {
        var document = Document();
        WorkspaceHas(document);
        ProvidersAre(NonProvider());

        var result = await Sut().GetAsync(document.Id.Uri.ToUri(), previousResultId: null, CancellationToken.None);

        Assert.AreEqual("v1.0", result.ResultId);
        Assert.IsFalse(result.Unchanged);
        Assert.IsEmpty(result.Diagnostics);
        await _parsing.DidNotReceive().ParseDocumentAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task PreviousResultIdMatchesVersion_ReturnsUnchanged()
    {
        var document = Document(version: 3);
        WorkspaceHas(document);
        ProvidersAre(Provider("RDCore.Diagnostics", 3, Diag(1)));

        var result = await Sut().GetAsync(document.Id.Uri.ToUri(), previousResultId: "v3.0", CancellationToken.None);

        Assert.AreEqual("v3.0", result.ResultId);
        Assert.IsTrue(result.Unchanged);
        Assert.IsEmpty(result.Diagnostics);
    }

    // a pull that comes while the workspace loads is answered with what is known then - what a module names of another that is not parsed yet is not - and
    // nothing changes for it while the load goes on. Once the workspace is loaded, a client that says it is asked to refresh its diagnostics is, and a pull
    // that names the result it had is answered anew.
    [TestMethod]
    public async Task APullBeforeTheWorkspaceIsLoaded_IsAnswered_AndTheClientIsAskedToPullAgainOnceItIs()
    {
        var document = Document();
        var uri = document.Id.Uri.ToUri();
        WorkspaceHas(document);
        ParseYields();
        ProvidersAre(Provider("RDCore.Diagnostics", 1, Diag(1)));
        var server = Substitute.For<OmniSharp.Extensions.LanguageServer.Protocol.Server.ILanguageServerFacade>();
        server.ClientSettings.Returns(new InitializeParams
        {
            Capabilities = new OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities.ClientCapabilities
            {
                Workspace = new OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities.WorkspaceClientCapabilities
                {
                    Diagnostics = new OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities.DiagnosticWorkspaceClientCapabilities { RefreshSupport = true },
                },
            },
        });
        _refresh.Attach(server);
        var parsed = new TaskCompletionSource();
        _parsing.ParseWorkspaceAsync(Arg.Any<CancellationToken>(), Arg.Any<IProgress<WorkProgressStep>?>()).Returns(parsed.Task);
        var sut = Sut();

        var loading = WorkspaceBringUp.LoadAsync(_parsing, _symbols, _refresh, Substitute.For<IWorkProgressReporter>(), expected: null, CancellationToken.None);
        var first = await sut.GetAsync(uri, previousResultId: null, CancellationToken.None);
        var whileLoading = await sut.GetAsync(uri, first.ResultId, CancellationToken.None);

        Assert.IsFalse(first.Unchanged, "a pull before the workspace is loaded is answered");
        Assert.IsTrue(whileLoading.Unchanged, "nothing changed while the workspace loads");
        server.DidNotReceive().SendRequest("workspace/diagnostic/refresh");

        parsed.SetResult();
        // a cap on a hang, not a budget: the wait is on the load itself, which a loaded machine finishes as late as it does.
        await loading.WaitAsync(TimeSpan.FromMinutes(5));
        var loaded = await sut.GetAsync(uri, first.ResultId, CancellationToken.None);

        server.Received(1).SendRequest("workspace/diagnostic/refresh");
        Assert.IsFalse(loaded.Unchanged, "what was found before the workspace was loaded is not what is found now");
    }

    // what the client's modules wait for is let go once the workspace is synced, and when its sync fails too: nothing waits for a bring-up that is over.
    [TestMethod]
    [DataRow(false, DisplayName = "the workspace is synced")]
    [DataRow(true, DisplayName = "its sync fails")]
    public async Task TheBringUp_LetsGoOfWhatWaitsForIt_OnceTheWorkspaceIsSynced_OrItsSyncFails(bool fails)
    {
        var order = new List<string>();
        var expected = Substitute.For<IDisposable>();
        expected.When(scope => scope.Dispose()).Do(_ => order.Add("let go"));
        _symbols.SyncWorkspaceAsync(Arg.Any<CancellationToken>(), Arg.Any<IProgress<WorkProgressStep>?>(), Arg.Any<bool>()).Returns(_ =>
        {
            order.Add("synced");
            return fails ? Task.FromException(new InvalidOperationException("no host")) : Task.CompletedTask;
        });

        var loading = WorkspaceBringUp.LoadAsync(_parsing, _symbols, _refresh, Substitute.For<IWorkProgressReporter>(), expected, CancellationToken.None);
        if (fails)
        {
            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => loading);
        }
        else
        {
            await loading;
        }

        CollectionAssert.AreEqual(new[] { "synced", "let go" }, order);
    }

    // an edit the program that waits does not have is a finding of the module it is in, where the edited declaration is now - an edit of the module as a whole on
    // its first line - and none of another's. The list that changed is a workspace that changed: a pull of a document that did not is not answered unchanged.
    [TestMethod]
    public async Task AnEditTheProgramThatWaitsDoesNotHave_IsAFindingOfItsModule_WhereTheEditIs()
    {
        var document = Document();
        WorkspaceHas(document);
        ParseYields();
        ProvidersAre(Provider("RDCore.Diagnostics", 1));
        var debugging = Substitute.For<IProgramDebugService>();
        debugging.EditStatusAsync(Arg.Any<CancellationToken>()).Returns(new DebugEditStatusResult
        {
            Suspended = true,
            Edits =
            [
                new DebugEdit
                {
                    Name = "Mod1.Foo", Kind = DebugEditKind.Restart, Reason = "a parameter was added", ModuleUri = new Uri("file:///c:/ws#Mod1"),
                    Range = new SourceRange(new SourcePosition(0, 0), new SourcePosition(1, 7)),
                },
                new DebugEdit { Name = "Mod1", Kind = DebugEditKind.Restart, Reason = "an option was changed", ModuleUri = new Uri("file:///c:/ws#Mod1") },
                new DebugEdit { Name = "Other.Bar", Kind = DebugEditKind.NotYet, Reason = "a local was added", ModuleUri = new Uri("file:///c:/ws#Other") },
            ],
        });
        var editStatus = new EditStatusMonitor(debugging, _refresh, NullLogger<EditStatusMonitor>.Instance);
        var generation = _refresh.Generation;

        Assert.IsTrue(await editStatus.CheckAsync(CancellationToken.None));
        var result = await Sut(editStatus).GetAsync(document.Id.Uri.ToUri(), previousResultId: $"v1.{generation}", CancellationToken.None);

        Assert.IsFalse(result.Unchanged, "the findings of the module are not what they were");
        var edits = result.Diagnostics.Where(diagnostic => diagnostic.Code?.String == "RDC20001").ToArray();
        Assert.HasCount(2, edits);
        Assert.IsTrue(edits.All(edit => edit.Severity == DiagnosticSeverity.Information));
        Assert.AreEqual("Mod1.Foo: a parameter was added", edits[0].Message);
        Assert.AreEqual(new Range(new Position(0, 0), new Position(1, 7)), edits[0].Range);
        Assert.AreEqual(new Range(new Position(0, 0), new Position(0, "Public Sub Foo()".Length)), edits[1].Range, "the module as a whole, on its first line");
    }

    // the module of an edit is named by its address, which escapes the name of a module named with an accent.
    [TestMethod]
    public async Task AnEditTheProgramThatWaitsDoesNotHave_IsAFindingOfItsModule_WhenTheModuleIsNamedWithAnAccent()
    {
        var document = new WorkspaceDocument("src/Modèle.bas", Root, "Public Sub Foo()\r\nEnd Sub", 1);
        WorkspaceHas(document);
        ParseYields();
        ProvidersAre(Provider("RDCore.Diagnostics", 1));
        var debugging = Substitute.For<IProgramDebugService>();
        debugging.EditStatusAsync(Arg.Any<CancellationToken>()).Returns(new DebugEditStatusResult
        {
            Suspended = true,
            Edits = [new DebugEdit { Name = "Modèle", Kind = DebugEditKind.Restart, Reason = "an option was changed", ModuleUri = SDK.Model.Symbols.SymbolUri.Module("file:///c:/ws", "Modèle") }],
        });
        var editStatus = new EditStatusMonitor(debugging, _refresh, NullLogger<EditStatusMonitor>.Instance);

        Assert.IsTrue(await editStatus.CheckAsync(CancellationToken.None));
        var result = await Sut(editStatus).GetAsync(document.Id.Uri.ToUri(), previousResultId: null, CancellationToken.None);

        Assert.AreEqual("Modèle: an option was changed", result.Diagnostics.Single(diagnostic => diagnostic.Code?.String == "RDC20001").Message);
    }

    [TestMethod]
    public async Task NoProgramThatWaits_HasNoEditItDoesNotHave()
    {
        var document = Document();
        WorkspaceHas(document);
        ParseYields();
        ProvidersAre(Provider("RDCore.Diagnostics", 1));
        var debugging = Substitute.For<IProgramDebugService>();
        debugging.EditStatusAsync(Arg.Any<CancellationToken>()).Returns(new DebugEditStatusResult());
        var editStatus = new EditStatusMonitor(debugging, _refresh, NullLogger<EditStatusMonitor>.Instance);

        Assert.IsFalse(await editStatus.CheckAsync(CancellationToken.None), "nothing changed");
        var result = await Sut(editStatus).GetAsync(document.Id.Uri.ToUri(), previousResultId: null, CancellationToken.None);

        Assert.IsFalse(result.Diagnostics.Any(diagnostic => diagnostic.Code?.String == "RDC20001"));
    }

    private async Task<EditStatusMonitor> OneEditListedAsync()
    {
        var debugging = Substitute.For<IProgramDebugService>();
        debugging.EditStatusAsync(Arg.Any<CancellationToken>()).Returns(new DebugEditStatusResult
        {
            Suspended = true,
            Edits = [new DebugEdit { Name = "Mod1.Foo", Kind = DebugEditKind.Restart, Reason = "a parameter was added", ModuleUri = new Uri("file:///c:/ws#Mod1") }],
        });
        var editStatus = new EditStatusMonitor(debugging, _refresh, NullLogger<EditStatusMonitor>.Instance);
        await editStatus.CheckAsync(CancellationToken.None);
        return editStatus;
    }

    // a client pushed to is published the static phase, then the static and runtime phases together: the edits are told by the static phase, once.
    [TestMethod]
    [DataRow(AnalysisPhase.Static, 1)]
    [DataRow(AnalysisPhase.Runtime, 0)]
    [DataRow(AnalysisPhase.All, 1)]
    public async Task TheEditsTheProgramThatWaitsDoesNotHave_AreToldByTheStaticPhase(AnalysisPhase phase, int told)
    {
        var document = Document();
        WorkspaceHas(document);
        ParseYields();
        ProvidersAre(Provider("RDCore.Diagnostics", 1));

        var result = await Sut(await OneEditListedAsync()).GetPhaseAsync(document.Id.Uri.ToUri(), phase, CancellationToken.None);

        Assert.AreEqual(told, result.Diagnostics.Count(diagnostic => diagnostic.Code?.String == "RDC20001"));
    }

    // the edits are the server's own finding: no extension is needed for it.
    [TestMethod]
    public async Task TheEditsTheProgramThatWaitsDoesNotHave_AreTold_WithNoExtensionThatFindsAnything()
    {
        var document = Document();
        WorkspaceHas(document);
        ProvidersAre(NonProvider());

        var result = await Sut(await OneEditListedAsync()).GetAsync(document.Id.Uri.ToUri(), previousResultId: null, CancellationToken.None);

        Assert.AreEqual(1, result.Diagnostics.Count(diagnostic => diagnostic.Code?.String == "RDC20001"));
    }

    // a procedure another module calls, removed: the findings of the module that calls it are not what they were, though its text is.
    [TestMethod]
    public async Task AnotherModuleChanged_TheFindingsOfTheSameVersion_AreFoundAgain()
    {
        var document = Document(version: 3);
        WorkspaceHas(document);
        ParseYields();
        ProvidersAre(Provider("RDCore.Diagnostics", 3, Diag(1)));
        _refresh.Changed();

        var result = await Sut().GetAsync(document.Id.Uri.ToUri(), previousResultId: "v3.0", CancellationToken.None);

        Assert.IsFalse(result.Unchanged);
        Assert.AreEqual("v3.1", result.ResultId);
        Assert.AreEqual(1, result.Diagnostics.Count);
    }

    [TestMethod]
    public async Task OneProvider_ReturnsItsDiagnostics()
    {
        var document = Document();
        WorkspaceHas(document);
        ParseYields();
        ProvidersAre(Provider("RDCore.Diagnostics", 1, Diag(1), Diag(2)));

        var result = await Sut().GetAsync(document.Id.Uri.ToUri(), previousResultId: null, CancellationToken.None);

        Assert.AreEqual("v1.0", result.ResultId);
        Assert.IsFalse(result.Unchanged);
        Assert.AreEqual(2, result.Diagnostics.Count);
    }

    [TestMethod]
    public async Task OverlappingDiagnosticsAcrossProviders_AreDeduped()
    {
        var document = Document();
        WorkspaceHas(document);
        ParseYields();
        var shared = Diag(1);
        ProvidersAre(
            Provider("RDCore.Diagnostics", 1, shared, Diag(2)),
            Provider("Other.Analyzer", 1, Diag(1)));

        var result = await Sut().GetAsync(document.Id.Uri.ToUri(), previousResultId: null, CancellationToken.None);

        Assert.AreEqual(2, result.Diagnostics.Count, "the diagnostic reported by both providers is collapsed");
    }

    [TestMethod]
    public async Task AnotherExtensionsFindings_AreItsOwn_OfThePrefixItDeclares_AndNoOtherReachesThePull()
    {
        var document = Document();
        WorkspaceHas(document);
        ParseYields();
        ProvidersAre(
            Provider("RDCore.Diagnostics", 1, Diag(1)),
            Provider(Manifest("Vendor", nameof(DiagnoseDocument), prefix: "XYZ"), 1, Diag(2, code: "XYZ00042", source: "RDCore"), Diag(3, code: "RDC00101")));

        var result = await Sut().GetAsync(document.Id.Uri.ToUri(), previousResultId: null, CancellationToken.None);

        CollectionAssert.AreEquivalent(new[] { ("VBC01027", "RDCore"), ("XYZ00042", "Vendor") },
            result.Diagnostics.Select(diagnostic => (diagnostic.Code!.Value.String, diagnostic.Source)).ToArray());
    }

    // a report of the text there was would carry an id the client sends again, and be answered unchanged for findings it never had.
    [TestMethod]
    public async Task DocumentMovesDuringFanOut_IsAnsweredContentModified_AndThePullAfterGetsEverything()
    {
        var before = Document(version: 1);
        var after = Document(version: 2);
        WorkspaceVersions(before, after);
        ParseYields();
        ProvidersAre(Provider("RDCore.Diagnostics", 1, Diag(1), Diag(2)));

        var moved = await Assert.ThrowsAsync<RpcErrorException>(() => Sut().GetAsync(before.Id.Uri.ToUri(), previousResultId: null, CancellationToken.None));
        var again = await Sut().GetAsync(before.Id.Uri.ToUri(), previousResultId: "v1", CancellationToken.None);

        Assert.AreEqual(ErrorCodes.ContentModified, moved.Code);
        Assert.IsFalse(again.Unchanged);
        Assert.AreEqual("v2.0", again.ResultId);
        Assert.AreEqual(2, again.Diagnostics.Count);
    }

    // what is pushed is of the version the document is at, which the lifecycle publishes nothing for until its own refresh of it.
    [TestMethod]
    public async Task DocumentMovesDuringAPhase_ThePhaseIsEmpty_OfTheVersionTheDocumentIsAt()
    {
        var before = Document(version: 1);
        var after = Document(version: 2);
        WorkspaceVersions(before, after);
        ParseYields();
        ProvidersAre(Provider("RDCore.Diagnostics", 1, Diag(1), Diag(2)));

        var result = await Sut().GetPhaseAsync(before.Id.Uri.ToUri(), AnalysisPhase.Static, CancellationToken.None);

        Assert.AreEqual("v2.0", result.ResultId);
        Assert.IsEmpty(result.Diagnostics);
    }

    [TestMethod]
    public async Task OneProviderThrows_ItsFailureIsSwallowed_OthersStillReport()
    {
        var document = Document();
        WorkspaceHas(document);
        ParseYields();

        var failing = Substitute.For<IRDCoreClientApp>();
        failing.ExtensionInfo.Returns(Manifest("Boom", nameof(DiagnoseDocument)));
        failing.WaitForReadyAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        failing.SendRequestAsync<DiagnoseDocumentParams, DiagnoseDocumentResult>(Arg.Any<DiagnoseDocumentParams>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("boom"));

        ProvidersAre(failing, Provider("RDCore.Diagnostics", 1, Diag(1)));

        var result = await Sut().GetAsync(document.Id.Uri.ToUri(), previousResultId: null, CancellationToken.None);

        Assert.AreEqual(1, result.Diagnostics.Count);
    }

    private IRDCoreClientApp HostThatProvidesSemantics(string json)
    {
        var host = Substitute.For<IRDCoreClientApp>();
        host.PlatformInfo.Returns(new PlatformInitializeResult { Provided = [nameof(SemanticAnalysis)] });
        host.SendRequestAsync<HostSemanticsParams, HostSemanticsResult>(Arg.Any<HostSemanticsParams>(), Arg.Any<CancellationToken>())
            .Returns(new HostSemanticsResult { Json = json });
        _orchestration.RuntimeEnvironment.Returns(host);
        return host;
    }

    private static DiagnoseDocumentPayload PayloadSentTo(IRDCoreClientApp provider)
    {
        var request = provider.ReceivedCalls().Single(call => call.GetMethodInfo().Name == nameof(IRDCoreClientApp.SendRequestAsync)).GetArguments()[0];
        return PlatformJson.Deserialize<DiagnoseDocumentPayload>(((DiagnoseDocumentParams)request!).Json);
    }

    [TestMethod]
    public async Task WhatTheHostFoundOutAboutTheModule_IsHandedToTheProvider()
    {
        var document = Document();
        WorkspaceHas(document);
        ParseYields();
        var module = new Uri("file://rdcore-test#Mod1");
        var host = HostThatProvidesSemantics(PlatformJson.Serialize(new SemanticsPayload([new ModuleSemanticsDto(module, true, [], [], [])])));
        var provider = Provider("RDCore.Diagnostics", 1);
        ProvidersAre(provider);

        await Sut().GetAsync(document.Id.Uri.ToUri(), previousResultId: null, CancellationToken.None);

        var semantics = PayloadSentTo(provider).Semantics;
        Assert.IsNotNull(semantics);
        Assert.IsTrue(semantics.OptionExplicit);
        await host.Received(1).SendRequestAsync<HostSemanticsParams, HostSemanticsResult>(
            Arg.Is<HostSemanticsParams>(request => request.ModuleName.Contains("Mod1")), Arg.Any<CancellationToken>());
    }

    // a document that is no file is no module of the host's: a module of the workspace by its name is another module, and what the host found of that one is
    // nothing of this one. Its syntax is what is analyzed.
    [TestMethod]
    public async Task ADocumentThatIsNoFile_IsAnalyzedForItsSyntax_AndTheHostIsNotAskedOfIt()
    {
        var untitled = new WorkspaceDocument(new Uri("untitled:Untitled-1"), Root, "Public Sub Foo()\r\nEnd Sub");
        WorkspaceHas(untitled);
        ParseYields();
        var host = HostThatProvidesSemantics(PlatformJson.Serialize(new SemanticsPayload([])));
        var provider = Provider("RDCore.Diagnostics", 1, Diag(1));
        ProvidersAre(provider);

        var result = await Sut().GetAsync(untitled.Id.Uri.ToUri(), previousResultId: null, CancellationToken.None);

        Assert.AreEqual(1, result.Diagnostics.Count);
        Assert.IsNull(PayloadSentTo(provider).Semantics);
        await host.DidNotReceiveWithAnyArgs().SendRequestAsync<HostSemanticsParams, HostSemanticsResult>(default!, default);
    }

    // a monitor of the program the host runs, which says it waits in break mode or not, as the host says when it is asked.
    private static (EditStatusMonitor Monitor, Func<bool, Task> Say) ProgramThatWaits(WorkspaceRefresh refresh)
    {
        var debugging = Substitute.For<IProgramDebugService>();
        var suspended = false;
        debugging.EditStatusAsync(Arg.Any<CancellationToken>()).Returns(_ => new DebugEditStatusResult { Suspended = suspended });
        var monitor = new EditStatusMonitor(debugging, refresh, NullLogger<EditStatusMonitor>.Instance);
        return (monitor, async waits =>
        {
            suspended = waits;
            _ = await monitor.CheckAsync(CancellationToken.None);
        });
    }

    // what comes of evaluating the code is not found out while a program waits in break mode: the host holds the redefinitions it would be of until the program
    // is run again. What the static phase finds is found as ever.
    [TestMethod]
    public async Task WhileAProgramWaitsInBreakMode_TheRuntimePhaseIsNeitherAskedOfTheHostNorOfTheProvider()
    {
        var document = Document();
        WorkspaceHas(document);
        ParseYields();
        var host = HostThatProvidesSemantics(PlatformJson.Serialize(new SemanticsPayload([new ModuleSemanticsDto(new Uri("file://rdcore-test#Mod1"), true, [], [], [])])));
        var provider = Provider("RDCore.Diagnostics", 1, Diag(1));
        ProvidersAre(provider);
        var (monitor, say) = ProgramThatWaits(_refresh);
        await say(true);
        var sut = Sut(monitor);

        var all = await sut.GetAsync(document.Id.Uri.ToUri(), previousResultId: null, CancellationToken.None);
        var runtime = await sut.GetPhaseAsync(document.Id.Uri.ToUri(), AnalysisPhase.Runtime, CancellationToken.None);

        Assert.AreEqual(AnalysisPhase.Static, PayloadSentTo(provider).Phase, "the extension is asked for the static phase alone");
        await host.Received(1).SendRequestAsync<HostSemanticsParams, HostSemanticsResult>(
            Arg.Is<HostSemanticsParams>(request => request.Phase == AnalysisPhase.Static), Arg.Any<CancellationToken>());
        await host.DidNotReceive().SendRequestAsync<HostSemanticsParams, HostSemanticsResult>(
            Arg.Is<HostSemanticsParams>(request => request.Phase.HasFlag(AnalysisPhase.Runtime)), Arg.Any<CancellationToken>());
        Assert.HasCount(1, all.Diagnostics, "the static phase's findings are found");
        Assert.IsEmpty(runtime.Diagnostics, "the runtime phase alone finds nothing, and asks no one");
    }

    // break mode begun withdraws what comes of evaluating the code at once - a client that pulls is asked to pull again, and a pull that names the result it had
    // is answered anew - and break mode ended brings it back the same way.
    [TestMethod]
    public async Task BreakModeBegunAndEnded_HasTheClientPullAgain_WithoutTheRuntimePhase_ThenWithIt()
    {
        var document = Document();
        var uri = document.Id.Uri.ToUri();
        WorkspaceHas(document);
        ParseYields();
        var host = HostThatProvidesSemantics(PlatformJson.Serialize(new SemanticsPayload([new ModuleSemanticsDto(new Uri("file://rdcore-test#Mod1"), true, [], [], [])])));
        ProvidersAre(Provider("RDCore.Diagnostics", 1, Diag(1)));
        var server = Substitute.For<OmniSharp.Extensions.LanguageServer.Protocol.Server.ILanguageServerFacade>();
        server.ClientSettings.Returns(new InitializeParams
        {
            Capabilities = new OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities.ClientCapabilities
            {
                Workspace = new OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities.WorkspaceClientCapabilities
                {
                    Diagnostics = new OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities.DiagnosticWorkspaceClientCapabilities { RefreshSupport = true },
                },
            },
        });
        _refresh.Attach(server);
        var (monitor, say) = ProgramThatWaits(_refresh);
        var sut = Sut(monitor);
        var running = await sut.GetAsync(uri, previousResultId: null, CancellationToken.None);

        await say(true);
        var waiting = await sut.GetAsync(uri, running.ResultId, CancellationToken.None);
        var refreshedAtBreak = server.ReceivedCalls().Count(call => call.GetArguments().FirstOrDefault() is "workspace/diagnostic/refresh");
        await say(false);
        var resumed = await sut.GetAsync(uri, waiting.ResultId, CancellationToken.None);
        var refreshedAtResume = server.ReceivedCalls().Count(call => call.GetArguments().FirstOrDefault() is "workspace/diagnostic/refresh");

        Assert.IsFalse(waiting.Unchanged, "a pull at the break is answered anew");
        Assert.IsFalse(resumed.Unchanged, "and once the program goes on");
        Assert.AreEqual(1, refreshedAtBreak, "the client is asked to pull again at the break");
        Assert.AreEqual(2, refreshedAtResume, "and again once the program goes on");
        var phases = host.ReceivedCalls().Select(call => call.GetArguments().FirstOrDefault()).OfType<HostSemanticsParams>().Select(request => request.Phase).ToArray();
        CollectionAssert.AreEqual(new[] { AnalysisPhase.All, AnalysisPhase.Static, AnalysisPhase.All }, phases);
    }

    [TestMethod]
    public async Task APhaseThatIsAskedFor_IsAskedOfTheHostAndOfTheProvider_AndOfNoOther()
    {
        var document = Document();
        WorkspaceHas(document);
        ParseYields();
        var host = HostThatProvidesSemantics(PlatformJson.Serialize(new SemanticsPayload([new ModuleSemanticsDto(new Uri("file://rdcore-test#Mod1"), true, [], [], [])])));
        var provider = Provider("RDCore.Diagnostics", 1, Diag(1));
        ProvidersAre(provider);

        var result = await Sut().GetPhaseAsync(document.Id.Uri.ToUri(), AnalysisPhase.Static, CancellationToken.None);

        Assert.AreEqual(AnalysisPhase.Static, PayloadSentTo(provider).Phase, "the extension is told which phase to find");
        await host.Received(1).SendRequestAsync<HostSemanticsParams, HostSemanticsResult>(
            Arg.Is<HostSemanticsParams>(request => request.Phase == AnalysisPhase.Static), Arg.Any<CancellationToken>());
        Assert.AreEqual(1, result.Diagnostics.Count);
    }

    [TestMethod]
    public async Task AnAnswerForEverything_IsAskedForEveryPhase_InOneRequestEach()
    {
        var document = Document();
        WorkspaceHas(document);
        ParseYields();
        var host = HostThatProvidesSemantics(PlatformJson.Serialize(new SemanticsPayload([])));
        var provider = Provider("RDCore.Diagnostics", 1);
        ProvidersAre(provider);

        await Sut().GetAsync(document.Id.Uri.ToUri(), previousResultId: null, CancellationToken.None);

        Assert.AreEqual(AnalysisPhase.All, PayloadSentTo(provider).Phase);
        await host.Received(1).SendRequestAsync<HostSemanticsParams, HostSemanticsResult>(
            Arg.Is<HostSemanticsParams>(request => request.Phase == AnalysisPhase.All), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task AHostThatDoesNotRunTheAnalysis_IsNotAsked_AndTheProviderHasNoSemantics()
    {
        var document = Document();
        WorkspaceHas(document);
        ParseYields();
        var host = Substitute.For<IRDCoreClientApp>();
        host.PlatformInfo.Returns(new PlatformInitializeResult { Provided = [] });
        _orchestration.RuntimeEnvironment.Returns(host);
        var provider = Provider("RDCore.Diagnostics", 1);
        ProvidersAre(provider);

        await Sut().GetAsync(document.Id.Uri.ToUri(), previousResultId: null, CancellationToken.None);

        Assert.IsNull(PayloadSentTo(provider).Semantics);
        await host.DidNotReceive().SendRequestAsync<HostSemanticsParams, HostSemanticsResult>(Arg.Any<HostSemanticsParams>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task AHostThatCannotAnswer_CostsTheProviderItsSemantics_AndNothingElse()
    {
        var document = Document();
        WorkspaceHas(document);
        ParseYields();
        var host = HostThatProvidesSemantics("{}");
        host.SendRequestAsync<HostSemanticsParams, HostSemanticsResult>(Arg.Any<HostSemanticsParams>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("the host is gone"));
        var provider = Provider("RDCore.Diagnostics", 1, Diag(1));
        ProvidersAre(provider);

        var result = await Sut().GetAsync(document.Id.Uri.ToUri(), previousResultId: null, CancellationToken.None);

        Assert.AreEqual(1, result.Diagnostics.Count);
        Assert.IsNull(PayloadSentTo(provider).Semantics);
    }

    private static readonly Uri FragmentUri = new("file://rdcore-test#Program");

    private void FragmentParses()
        => _parsing.ParseFragmentAsync(Arg.Any<Uri>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ModuleParser().Parse(TestUri.TestModuleUri(), "Public Sub Foo()\r\nEnd Sub"));

    // the module is named in the host as its code names it: its address escapes a name with an accent.
    [TestMethod]
    [DataRow("Program", DisplayName = "an ASCII name")]
    [DataRow("Modèle", DisplayName = "a name with an accented letter")]
    public async Task AModuleTheClientSupplied_IsPutInTheHost_DefinedAndLoaded_BeforeItIsAskedAbout(string name)
    {
        var module = SDK.Model.Symbols.SymbolUri.Module(new Uri("file://rdcore-test"), name);
        FragmentParses();
        var host = HostThatProvidesSemantics(PlatformJson.Serialize(new SemanticsPayload([new ModuleSemanticsDto(module, false, [], [], [])])));
        var provider = Provider("RDCore.Diagnostics", 0);
        ProvidersAre(provider);

        await Sut().AnalyzeFragmentAsync(module, "Public Sub Foo()\r\nEnd Sub", CancellationToken.None);

        Received.InOrder(() =>
        {
            _ = _symbols.SyncModuleAsync(name, Arg.Any<ModuleParseResult>(), Arg.Any<CancellationToken>());
            _ = _symbols.LoadModuleCodeAsync(name, Arg.Any<ModuleParseResult>(), Arg.Any<CancellationToken>());
            _ = host.SendRequestAsync<HostSemanticsParams, HostSemanticsResult>(
                Arg.Is<HostSemanticsParams>(request => request.ModuleName == name), Arg.Any<CancellationToken>());
        });
        Assert.AreEqual(false, PayloadSentTo(provider).Semantics!.OptionExplicit);
    }

    [TestMethod]
    public async Task AModuleTheClientSupplied_IsNotPutInAHostThatDoesNotRunTheAnalysis()
    {
        FragmentParses();
        var host = Substitute.For<IRDCoreClientApp>();
        host.PlatformInfo.Returns(new PlatformInitializeResult { Provided = [] });
        _orchestration.RuntimeEnvironment.Returns(host);
        var provider = Provider("RDCore.Diagnostics", 0);
        ProvidersAre(provider);

        await Sut().AnalyzeFragmentAsync(FragmentUri, "Public Sub Foo()\r\nEnd Sub", CancellationToken.None);

        await _symbols.DidNotReceive().SyncModuleAsync(Arg.Any<string>(), Arg.Any<ModuleParseResult>(), Arg.Any<CancellationToken>());
        Assert.IsNull(PayloadSentTo(provider).Semantics);
    }

    // what an analysis reports of its progress, as it reports it.
    private sealed class Steps : IProgress<WorkProgressStep>
    {
        public List<WorkProgressStep> Reported { get; } = [];

        public void Report(WorkProgressStep value)
        {
            lock (Reported)
            {
                Reported.Add(value);
            }
        }
    }

    [TestMethod]
    public async Task AnAnalysis_ReportsItsSteps_TheParse_TheSemanticModel_AndEachProviderAsItAnswers()
    {
        FragmentParses();
        HostThatProvidesSemantics(PlatformJson.Serialize(new SemanticsPayload([new ModuleSemanticsDto(FragmentUri, false, [], [], [])])));
        ProvidersAre(Provider("RDCore.Diagnostics", 0), Provider("Another", 0));
        var steps = new Steps();

        await Sut().AnalyzeFragmentAsync(FragmentUri, "Public Sub Foo()\r\nEnd Sub", CancellationToken.None, steps);

        var percentages = steps.Reported.Select(step => step.Percentage ?? -1).ToArray();
        CollectionAssert.AreEqual(percentages.Order().ToArray(), percentages, "the percentage only goes up");
        Assert.AreEqual(100, percentages[^1], "every provider answered");
        Assert.AreEqual(5, steps.Reported.Count, "the parse, the model, the providers asked, and each that answered");
        StringAssert.Contains(steps.Reported[^1].Message, "2");
    }

    [TestMethod]
    public async Task AHostThatCannotBeTold_OfAModuleTheClientSupplied_CostsTheProviderItsSemantics_AndNothingElse()
    {
        FragmentParses();
        HostThatProvidesSemantics("{}");
        _symbols.SyncModuleAsync(Arg.Any<string>(), Arg.Any<ModuleParseResult>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("the host is gone"));
        var provider = Provider("RDCore.Diagnostics", 0, Diag(1));
        ProvidersAre(provider);

        var (diagnostics, _) = await Sut().AnalyzeFragmentAsync(FragmentUri, "Public Sub Foo()\r\nEnd Sub", CancellationToken.None);

        Assert.AreEqual(1, diagnostics.Count);
        Assert.IsNull(PayloadSentTo(provider).Semantics);
    }

    [TestMethod]
    public async Task AProgramOfTheBasic_HasItsDiagnosticsWhereTheyAreInTheText_NotInTheModuleItIs()
    {
        var program = new WorkspaceDocument("src/hello.rdc", Root, "100 X = 1\r\n110 Y = 2\r\n", version: 1);
        WorkspaceHas(program);
        _parsing.ParseDocumentAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>())
            .Returns(new ModuleParser().Parse(TestUri.TestModuleUri(), "Public Sub Main()\r\n100 X = 1\r\n110 Y = 2\r\nEnd Sub"));
        // the module has a header line the text does not: line 2 of the module is line 1 of the text.
        ProvidersAre(Provider("RDCore.Diagnostics", 1, Diag(2)));

        var result = await Sut().GetAsync(program.Id.Uri.ToUri(), previousResultId: null, CancellationToken.None);

        Assert.AreEqual(1, result.Diagnostics.Single().Range.Start.Line);
    }
}
