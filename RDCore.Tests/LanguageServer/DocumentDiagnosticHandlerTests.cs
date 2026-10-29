using NSubstitute;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.JsonRpc.Server;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Diagnostics;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Progress;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Tests.LanguageServer;

[TestClass]
public sealed class DocumentDiagnosticHandlerTests
{
    private static readonly Uri DocUri = new("file:///c:/ws/src/Mod1.bas");

    private readonly IDocumentDiagnosticsService _service = Substitute.For<IDocumentDiagnosticsService>();
    private readonly IDocumentLifecycleService _lifecycle = Substitute.For<IDocumentLifecycleService>();

    private readonly IncomingDocumentChanges _incoming = new();

    private DocumentDiagnosticHandler Sut() => new(_service, _lifecycle, new WorkProgressReporter(), _incoming);

    // what is found is of the text the host has: the pull waits for the host to have the document as it is, then analyzes it.
    [TestMethod]
    public async Task APull_WaitsForTheHostToHaveTheDocument_ThenAnalyzesIt()
    {
        var waiting = new TaskCompletionSource();
        var synced = new TaskCompletionSource();
        _lifecycle.WhenSyncedAsync(DocUri, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            waiting.TrySetResult();
            return synced.Task;
        });
        _service.GetAsync(DocUri, null, Arg.Any<CancellationToken>(), Arg.Any<IProgress<WorkProgressStep>?>()).Returns(DocumentDiagnosticsResult.Fresh(2, []));

        var pull = Sut().Handle(Request(), CancellationToken.None);
        await waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await _service.DidNotReceiveWithAnyArgs().GetAsync(default!, default, default);
        synced.SetResult();
        await pull.WaitAsync(TimeSpan.FromSeconds(5));
        await _service.Received(1).GetAsync(DocUri, null, Arg.Any<CancellationToken>(), Arg.Any<IProgress<WorkProgressStep>?>());
    }

    // an edit that arrives while the pull waits for the host ends it, as one that arrives while it analyzes does.
    [TestMethod]
    public async Task AnEditWhileThePullWaitsForTheHost_EndsIt_ContentModified()
    {
        var waiting = new TaskCompletionSource();
        _lifecycle.WhenSyncedAsync(DocUri, Arg.Any<CancellationToken>()).Returns(call =>
        {
            waiting.TrySetResult();
            return Task.Delay(Timeout.Infinite, call.Arg<CancellationToken>());
        });

        var pull = Sut().Handle(Request(), CancellationToken.None);
        await waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));
        _incoming.Arrived(DocUri);

        var refused = await Assert.ThrowsExactlyAsync<RpcErrorException>(() => pull.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.AreEqual(ErrorCodes.ContentModified, refused.Code);
        await _service.DidNotReceiveWithAnyArgs().GetAsync(default!, default, default);
    }

    private static DocumentDiagnosticParams Request(string? previousResultId = null) => new()
    {
        TextDocument = new TextDocumentIdentifier(DocUri),
        PreviousResultId = previousResultId,
    };

    [TestMethod]
    public async Task FreshResult_MapsToARelatedFullReport_ForwardingTheDiagnostics()
    {
        var diagnostic = new Diagnostic
        {
            Code = new DiagnosticCode("VBC01027"),
            Source = "RDCore",
            Message = "Syntax error",
            Severity = DiagnosticSeverity.Error,
            Range = new Range(new Position(3, 4), new Position(3, 18)),
        };
        _service.GetAsync(DocUri, null, Arg.Any<CancellationToken>(), Arg.Any<IProgress<WorkProgressStep>?>())
            .Returns(DocumentDiagnosticsResult.Fresh(2, [diagnostic]));

        var report = await Sut().Handle(Request(), CancellationToken.None);

        var full = (RelatedFullDocumentDiagnosticReport)report;
        Assert.AreEqual("v2.0", full.ResultId);
        Assert.AreSame(diagnostic, full.Items.Single());
    }

    [TestMethod]
    public async Task UnchangedResult_MapsToARelatedUnchangedReport()
    {
        _service.GetAsync(DocUri, "v5.0", Arg.Any<CancellationToken>(), Arg.Any<IProgress<WorkProgressStep>?>())
            .Returns(DocumentDiagnosticsResult.NotChanged(5));

        var report = await Sut().Handle(Request("v5.0"), CancellationToken.None);

        var unchanged = (RelatedUnchangedDocumentDiagnosticReport)report;
        Assert.AreEqual("v5.0", unchanged.ResultId);
    }

    [TestMethod]
    public void RegistrationOptions_IdentifyAsRdcore_WithInterFileDependencies_AndWorkspaceDiagnostics()
    {
        var registration = (IRegistration<DiagnosticsRegistrationOptions, DiagnosticClientCapabilities>)Sut();

        var options = registration.GetRegistrationOptions(new(), new());

        Assert.AreEqual("rdcore", options.Identifier);
        Assert.IsTrue(options.InterFileDependencies, "a module's diagnostics depend on what the others declare");
        Assert.IsTrue(options.WorkspaceDiagnostics, "workspace/diagnostic is served (WorkspaceDiagnosticHandler)");
    }
}
