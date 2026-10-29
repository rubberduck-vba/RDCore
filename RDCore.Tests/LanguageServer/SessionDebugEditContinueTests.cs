using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RDCore.LanguageServer.Debugging;
using RDCore.LanguageServer.Runtime;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Client;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Server.Services;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// A program that waits is asked to go on - continued, stepped, moved - against the workspace as the documents are now: the request is relayed to the host once
/// what the edits made while it waited are is loaded, the rest of the workspace defined again against them included, and the host checks the program against that.
/// </summary>
[TestClass]
public sealed class SessionDebugEditContinueTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private readonly IProgramDebugService _debugging = Substitute.For<IProgramDebugService>();
    private readonly IPlatformClientCapabilitiesService _capabilities = Substitute.For<IPlatformClientCapabilitiesService>();
    private readonly IDocumentLifecycleService _lifecycle = Substitute.For<IDocumentLifecycleService>();
    private readonly EditStatusMonitor _editStatus;

    // the edit the host refuses the program for once the whole workspace is loaded: a name the edited module used is gone from another.
    private static readonly HostDebugEdit DoesNotCompile = new()
    {
        Name = "M1.Caller", Kind = HostDebugEditKind.DoesNotCompile, Reason = "Sub or Function not defined: Helper", ModuleUri = new Uri("file:///c:/ws#M1"),
    };

    public SessionDebugEditContinueTests()
    {
        _capabilities.Expects(Arg.Any<Func<LanguageServerCapabilities, CorePlatformClientCapability>>()).Returns(true);
        _debugging.EditStatusAsync(Arg.Any<CancellationToken>()).Returns(new HostDebugEditStatusResult());
        _editStatus = new EditStatusMonitor(_debugging, Substitute.For<IWorkspaceRefresh>(), NullLogger<EditStatusMonitor>.Instance);
    }

    // the load of the documents as they are: what the relay waits for, which a test lets go of.
    private (TaskCompletionSource Asked, TaskCompletionSource Loaded) LoadPending()
    {
        var (asked, loaded) = (new TaskCompletionSource(), new TaskCompletionSource());
        _lifecycle.WhenAllLoadedAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            asked.TrySetResult();
            return loaded.Task;
        });

        return (asked, loaded);
    }

    [TestMethod]
    public async Task AContinue_IsRelayedOnceTheWorkspaceIsLoaded_AndIsAnsweredWhatTheHostChecksItAgainst()
    {
        var (asked, loaded) = LoadPending();
        _debugging.ResumeAsync(null, Arg.Any<CancellationToken>()).Returns(new ExecuteSessionResult
        {
            Outcome = ExecutionOutcome.Refused,
            ErrorMessage = $"{DoesNotCompile.Name}: {DoesNotCompile.Reason}",
        });
        var handler = new SessionDebugResumeHandler(_debugging, _capabilities, _editStatus, _lifecycle);

        var answer = handler.Handle(new SessionDebugResumeParams(), CancellationToken.None);
        await asked.Task.WaitAsync(Patience);

        await _debugging.DidNotReceiveWithAnyArgs().ResumeAsync(default, default);
        loaded.SetResult();
        var result = await answer.WaitAsync(Patience);

        Assert.AreEqual(ExecutionOutcome.Refused, result.Outcome);
        StringAssert.Contains(result.ErrorMessage, "Helper");
        await _debugging.Received(1).ResumeAsync(null, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task AGoto_IsRelayedOnceTheWorkspaceIsLoaded()
    {
        var (asked, loaded) = LoadPending();
        _debugging.GotoAsync(Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(new HostDebugGotoResult { Moved = false, Reason = DoesNotCompile.Reason });
        var handler = new SessionDebugGotoHandler(_debugging, _capabilities, _editStatus, _lifecycle);

        var answer = handler.Handle(new SessionDebugGotoParams { Line = 3 }, CancellationToken.None);
        await asked.Task.WaitAsync(Patience);

        await _debugging.DidNotReceiveWithAnyArgs().GotoAsync(default, default, default);
        loaded.SetResult();
        Assert.IsFalse((await answer.WaitAsync(Patience)).Moved);
    }

    // what is said of the edits is of the workspace as the documents are, and so is what the diagnostics of the edited modules are of.
    [TestMethod]
    public async Task TheEditStatus_IsAskedOnceTheWorkspaceIsLoaded_AndIsWhatTheDiagnosticsAreOf()
    {
        var (asked, loaded) = LoadPending();
        var listed = new HostDebugEditStatusResult { Suspended = true, Edits = [DoesNotCompile] };
        var handler = new SessionDebugEditStatusHandler(_capabilities, _editStatus, _lifecycle);

        var answer = handler.Handle(new SessionDebugEditStatusParams(), CancellationToken.None);
        await asked.Task.WaitAsync(Patience);

        await _debugging.DidNotReceiveWithAnyArgs().EditStatusAsync(default);
        _debugging.EditStatusAsync(Arg.Any<CancellationToken>()).Returns(listed);
        loaded.SetResult();

        Assert.AreEqual(DoesNotCompile, (await answer.WaitAsync(Patience)).Edits.Single());
        Assert.AreEqual(DoesNotCompile, _editStatus.Current.Edits.Single());
    }

    // nothing to wait for: the request is relayed as it comes.
    [TestMethod]
    public async Task AContinue_WithNoLoadPending_IsRelayedAtOnce()
    {
        _lifecycle.WhenAllLoadedAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _debugging.ResumeAsync(null, Arg.Any<CancellationToken>()).Returns(new ExecuteSessionResult { Outcome = ExecutionOutcome.Suspended });
        var handler = new SessionDebugResumeHandler(_debugging, _capabilities, _editStatus, _lifecycle);

        Assert.AreEqual(ExecutionOutcome.Suspended, (await handler.Handle(new SessionDebugResumeParams(), CancellationToken.None).WaitAsync(Patience)).Outcome);
    }

    // a program that is over, or went on with every edit, has none it does not have: what was said is cleared once it is answered.
    [TestMethod]
    public async Task AContinue_ThatIsAnswered_HasTheEditsAskedAgain()
    {
        _lifecycle.WhenAllLoadedAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _debugging.EditStatusAsync(Arg.Any<CancellationToken>()).Returns(new HostDebugEditStatusResult { Suspended = true, Edits = [DoesNotCompile] });
        await _editStatus.CheckAsync(CancellationToken.None);
        _debugging.ResumeAsync(null, Arg.Any<CancellationToken>()).Returns(new ExecuteSessionResult { Outcome = ExecutionOutcome.Completed });
        _debugging.EditStatusAsync(Arg.Any<CancellationToken>()).Returns(new HostDebugEditStatusResult());
        var handler = new SessionDebugResumeHandler(_debugging, _capabilities, _editStatus, _lifecycle);

        _ = await handler.Handle(new SessionDebugResumeParams(), CancellationToken.None).WaitAsync(Patience);

        Assert.IsEmpty(_editStatus.Current.Edits);
    }

    // a program ended at the stop - an End typed in it, a discard that ends it - has no edits it does not have; the host has answered, and a request cancelled
    // after it is still answered what the host said.
    [TestMethod]
    [DataRow("execute")]
    [DataRow("discard")]
    public async Task AProgramEndedAtTheStop_HasTheEditsAskedAgain_EvenWhenTheRequestIsCancelledOnceAnswered(string request)
    {
        _debugging.EditStatusAsync(Arg.Any<CancellationToken>()).Returns(new HostDebugEditStatusResult { Suspended = true, Edits = [DoesNotCompile] });
        await _editStatus.CheckAsync(CancellationToken.None);
        _debugging.EditStatusAsync(Arg.Any<CancellationToken>()).Returns(new HostDebugEditStatusResult());
        using var cancelled = new CancellationTokenSource();
        var symbols = Substitute.For<RDCore.LanguageServer.Symbols.ISymbolSyncService>();
        _debugging.ExecuteAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            cancelled.Cancel();
            return new HostDebugEvaluateResult { Value = string.Empty };
        });
        symbols.DiscardModuleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>(), true).Returns(_ =>
        {
            cancelled.Cancel();
            return new DiscardSessionResult { Discarded = 1 };
        });

        object answer = request == "execute"
            ? await new SessionDebugExecuteHandler(_debugging, _capabilities, _editStatus).Handle(new SessionDebugExecuteParams { Statement = "End" }, cancelled.Token)
            : await new SessionDiscardHandler(symbols, _capabilities, _editStatus).Handle(new DiscardSessionParams { ModuleName = "M1", EndProgram = true }, cancelled.Token);

        Assert.IsNotNull(answer);
        Assert.IsEmpty(_editStatus.Current.Edits);
    }
}
