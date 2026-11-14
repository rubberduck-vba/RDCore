using NSubstitute;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.JsonRpc.Server;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Diagnostics;
using RDCore.LanguageServer.Runtime;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Progress;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Tests.LanguageServer;

[TestClass]
public sealed class DocumentDiagnosticHandlerTests
{
    // a cap on a hang, not a budget: each wait is on the event itself, which a loaded machine brings as late as it does.
    private static readonly TimeSpan SafetyCap = TimeSpan.FromMinutes(5);

    private static readonly Uri DocUri = new("file:///c:/ws/src/Mod1.bas");

    private readonly IDocumentDiagnosticsService _service = Substitute.For<IDocumentDiagnosticsService>();
    private readonly IDocumentLifecycleService _lifecycle = Substitute.For<IDocumentLifecycleService>();

    private readonly IncomingDocumentChanges _incoming = new();
    private readonly OpenDialogs _dialogs = new();

    private DocumentDiagnosticHandler Sut() => new(_service, _lifecycle, new WorkProgressReporter(), _incoming, _dialogs);

    // a program that waits for its client to answer a dialog: the host takes no other text until it is answered, and the program is over then.
    private (Task Asked, TaskCompletionSource<int> Answer) ProgramWaitingForADialog()
    {
        var answer = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        _lifecycle.WhenSyncedAsync(DocUri, Arg.Any<CancellationToken>()).Returns(call => answer.Task.WaitAsync(call.Arg<CancellationToken>()));
        _service.GetAsync(DocUri, null, Arg.Any<CancellationToken>(), Arg.Any<IProgress<WorkProgressStep>?>()).Returns(DocumentDiagnosticsResult.Fresh(2, []));
        return (_dialogs.WhileOpenAsync(() => answer.Task), answer);
    }

    private static void AssertDeclinedNotRetriggered(RpcErrorException declined)
    {
        Assert.AreEqual(ErrorCodes.ServerCancelled, declined.Code);
        Assert.IsFalse(Assert.IsInstanceOfType<DiagnosticServerCancellationData>(declined.Error).RetriggerRequest,
            "a client that retriggers would ask again and again while the dialog is open");
    }

    // the program waits for the client, and the pull would wait for the program: it is declined at once instead.
    [TestMethod]
    public async Task APullWhileADialogIsOpen_IsDeclinedAtOnce_ServerCancelled_AndNotRetriggered()
    {
        var (_, answer) = ProgramWaitingForADialog();

        var declined = await Assert.ThrowsExactlyAsync<RpcErrorException>(() => Sut().Handle(Request(), CancellationToken.None).WaitAsync(SafetyCap));

        AssertDeclinedNotRetriggered(declined);
        await _service.DidNotReceiveWithAnyArgs().GetAsync(default!, default, default);
        answer.SetResult(1);
    }

    [TestMethod]
    public async Task APullThatWaitsForTheHost_WhenADialogOpens_IsDeclined()
    {
        var waiting = new TaskCompletionSource();
        _lifecycle.WhenSyncedAsync(DocUri, Arg.Any<CancellationToken>()).Returns(call =>
        {
            waiting.TrySetResult();
            return Task.Delay(Timeout.Infinite, call.Arg<CancellationToken>());
        });
        var pull = Sut().Handle(Request(), CancellationToken.None);
        await waiting.Task.WaitAsync(SafetyCap);

        var answer = new TaskCompletionSource<int>();
        _ = _dialogs.WhileOpenAsync(() => answer.Task);

        AssertDeclinedNotRetriggered(await Assert.ThrowsExactlyAsync<RpcErrorException>(() => pull.WaitAsync(SafetyCap)));
        answer.SetResult(1);
    }

    // the client is not told to pull again of its own accord: it is asked to once the dialog is answered, and its pull is answered then.
    [TestMethod]
    public async Task OnceTheDialogIsAnswered_TheClientIsAskedToPullAgain_AndItsPullIsAnswered()
    {
        var pullAgain = 0;
        _dialogs.PullAgain = () => Interlocked.Increment(ref pullAgain);
        var (asked, answer) = ProgramWaitingForADialog();
        await Assert.ThrowsExactlyAsync<RpcErrorException>(() => Sut().Handle(Request(), CancellationToken.None).WaitAsync(SafetyCap));
        Assert.AreEqual(0, pullAgain, "not while the dialog is open");

        answer.SetResult(1);
        await asked.WaitAsync(SafetyCap);

        Assert.AreEqual(1, pullAgain, "once it is answered");
        var report = await Sut().Handle(Request(), CancellationToken.None).WaitAsync(SafetyCap);
        Assert.AreEqual("v2.0", ((RelatedFullDocumentDiagnosticReport)report).ResultId);
    }

    [TestMethod]
    public async Task ADialogThatDeclinedNoPull_AsksTheClientForNothing()
    {
        var pullAgain = 0;
        _dialogs.PullAgain = () => Interlocked.Increment(ref pullAgain);
        var (asked, answer) = ProgramWaitingForADialog();

        answer.SetResult(1);
        await asked.WaitAsync(SafetyCap);

        Assert.AreEqual(0, pullAgain);
    }

    // a dialog the client fails to answer is closed all the same: the pull it declined is made again.
    [TestMethod]
    public async Task ADialogTheClientFailsToAnswer_AsksTheClientToPullAgain()
    {
        var pullAgain = 0;
        _dialogs.PullAgain = () => Interlocked.Increment(ref pullAgain);
        var (asked, answer) = ProgramWaitingForADialog();
        await Assert.ThrowsExactlyAsync<RpcErrorException>(() => Sut().Handle(Request(), CancellationToken.None).WaitAsync(SafetyCap));

        answer.SetException(new InvalidOperationException("the client is gone"));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => asked.WaitAsync(SafetyCap));

        Assert.AreEqual(1, pullAgain);
    }

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
        await waiting.Task.WaitAsync(SafetyCap);

        await _service.DidNotReceiveWithAnyArgs().GetAsync(default!, default, default);
        synced.SetResult();
        await pull.WaitAsync(SafetyCap);
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
        await waiting.Task.WaitAsync(SafetyCap);
        _incoming.Arrived(DocUri);

        var refused = await Assert.ThrowsExactlyAsync<RpcErrorException>(() => pull.WaitAsync(SafetyCap));
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
