using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer;
using RDCore.LanguageServer.Diagnostics;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.Parsing;
using RDCore.SDK.Client;
using RDCore.SDK.Extensibility;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Platform.Protocol;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Tests.LanguageServer;

[TestClass]
public sealed class DocumentDiagnosticsServiceTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-diag-ws");

    private readonly IWorkspaceDocumentService _documents = Substitute.For<IWorkspaceDocumentService>();
    private readonly IParsingClientService _parsing = Substitute.For<IParsingClientService>();
    private readonly IPlatformOrchestrationService _orchestration = Substitute.For<IPlatformOrchestrationService>();
    private readonly ISymbolSyncService _symbols = Substitute.For<ISymbolSyncService>();

    private DocumentDiagnosticsService Sut()
        => new(_documents, _parsing, _orchestration, _symbols, NullLogger<DocumentDiagnosticsService>.Instance);

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

    private IRDCoreClientApp Provider(string title, int sourceVersion, params Diagnostic[] diagnostics)
    {
        var app = Substitute.For<IRDCoreClientApp>();
        app.ExtensionInfo.Returns(new ExtensionInfo(
            $"{title}.exe", title, new Version(1, 0), "publisher", "https://example.test", "desc", "sig",
            [new PlatformExtensionServerCapability(nameof(DiagnoseDocument))]));
        app.WaitForReadyAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        app.SendRequestAsync<DiagnoseDocumentRequest, DiagnoseDocumentResponse>(Arg.Any<DiagnoseDocumentRequest>(), Arg.Any<CancellationToken>())
            .Returns(new DiagnoseDocumentResponse { Diagnostics = diagnostics, SourceVersion = sourceVersion });
        return app;
    }

    private static IRDCoreClientApp NonProvider()
    {
        var app = Substitute.For<IRDCoreClientApp>();
        app.ExtensionInfo.Returns(new ExtensionInfo(
            "other.exe", "Other", new Version(1, 0), "publisher", "https://example.test", "desc", "sig",
            [new PlatformExtensionServerCapability("CliCommand")]));
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

        Assert.AreEqual("v1", result.ResultId);
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

        var result = await Sut().GetAsync(document.Id.Uri.ToUri(), previousResultId: "v3", CancellationToken.None);

        Assert.AreEqual("v3", result.ResultId);
        Assert.IsTrue(result.Unchanged);
        Assert.IsEmpty(result.Diagnostics);
    }

    [TestMethod]
    public async Task OneProvider_ReturnsItsDiagnostics()
    {
        var document = Document();
        WorkspaceHas(document);
        ParseYields();
        ProvidersAre(Provider("RDCore.Diagnostics", 1, Diag(1), Diag(2)));

        var result = await Sut().GetAsync(document.Id.Uri.ToUri(), previousResultId: null, CancellationToken.None);

        Assert.AreEqual("v1", result.ResultId);
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
    public async Task DocumentMovesDuringFanOut_StaleDiagnosticsAreDropped()
    {
        var before = Document(version: 1);
        var after = Document(version: 2);
        WorkspaceVersions(before, after);
        ParseYields();
        ProvidersAre(Provider("RDCore.Diagnostics", 1, Diag(1), Diag(2)));

        var result = await Sut().GetAsync(before.Id.Uri.ToUri(), previousResultId: null, CancellationToken.None);

        Assert.AreEqual("v2", result.ResultId, "the report id advances to the version the document is now at");
        Assert.IsFalse(result.Unchanged);
        Assert.IsEmpty(result.Diagnostics);
    }

    [TestMethod]
    public async Task OneProviderThrows_ItsFailureIsSwallowed_OthersStillReport()
    {
        var document = Document();
        WorkspaceHas(document);
        ParseYields();

        var failing = Substitute.For<IRDCoreClientApp>();
        failing.ExtensionInfo.Returns(new ExtensionInfo(
            "boom.exe", "Boom", new Version(1, 0), "publisher", "https://example.test", "desc", "sig",
            [new PlatformExtensionServerCapability(nameof(DiagnoseDocument))]));
        failing.WaitForReadyAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        failing.SendRequestAsync<DiagnoseDocumentRequest, DiagnoseDocumentResponse>(Arg.Any<DiagnoseDocumentRequest>(), Arg.Any<CancellationToken>())
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
        return PlatformJson.Deserialize<DiagnoseDocumentPayload>(((DiagnoseDocumentRequest)request!).Json);
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

    [TestMethod]
    public async Task AModuleTheClientSupplied_IsPutInTheHost_DefinedAndLoaded_BeforeItIsAskedAbout()
    {
        FragmentParses();
        var host = HostThatProvidesSemantics(PlatformJson.Serialize(new SemanticsPayload([new ModuleSemanticsDto(FragmentUri, false, [], [], [])])));
        var provider = Provider("RDCore.Diagnostics", 0);
        ProvidersAre(provider);

        await Sut().AnalyzeFragmentAsync(FragmentUri, "Public Sub Foo()\r\nEnd Sub", CancellationToken.None);

        Received.InOrder(() =>
        {
            _symbols.SyncModuleAsync("Program", Arg.Any<ModuleParseResult>(), Arg.Any<CancellationToken>());
            _symbols.LoadModuleCodeAsync("Program", Arg.Any<ModuleParseResult>(), Arg.Any<CancellationToken>());
            host.SendRequestAsync<HostSemanticsParams, HostSemanticsResult>(
                Arg.Is<HostSemanticsParams>(request => request.ModuleName == "Program"), Arg.Any<CancellationToken>());
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
}
