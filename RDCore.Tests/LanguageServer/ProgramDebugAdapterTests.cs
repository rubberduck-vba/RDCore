using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OmniSharp.Extensions.DebugAdapter.Client;
using OmniSharp.Extensions.DebugAdapter.Protocol.Events;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;
using OmniSharp.Extensions.DebugAdapter.Server;
using OmniSharp.Extensions.JsonRpc.Server;
using RDCore.LanguageServer.Debugging;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using System.Collections.Concurrent;
using System.IO.Pipelines;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// A debugger client speaks <strong>DAP</strong> to the adapter, over the same server the language server's executable runs in <c>--dap</c> mode, and the adapter has the
/// platform do what the client asks: a file is a module, a line is one less, and where the program waits is an event.
/// </summary>
[TestClass]
public sealed class ProgramDebugAdapterTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);
    private static readonly string Path = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dap-ws", "Prog.bas"));

    private sealed class Session : IAsyncDisposable
    {
        private readonly ConcurrentQueue<object> _events = new();
        private readonly TaskCompletionSource<string> _lost = new();

        public IDebugWorkspace Workspace { get; } = Substitute.For<IDebugWorkspace>();
        public IProgramDebugService Debugging { get; } = Substitute.For<IProgramDebugService>();
        public HostOutputRelay Relay { get; } = new();

        // what the program that waits does not have of the edits, as the host says it: nothing, unless a test says otherwise.
        public EditStatusMonitor EditStatus { get; }
        public ProgramDebugAdapter Adapter { get; }
        public DebugAdapterServer Server { get; private set; } = default!;
        public DebugAdapterClient Client { get; private set; } = default!;

        public Session()
        {
            Workspace.OpenAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
            Workspace.Lost.Returns(_lost.Task);
            Workspace.TryGetPath("Prog", out Arg.Any<string>()).Returns(call =>
            {
                call[1] = Path;
                return true;
            });
            Workspace.TryGetPath(Arg.Is<string>(name => name != "Prog"), out Arg.Any<string>()).Returns(false);
            Workspace.TryGetModule(Path, out Arg.Any<string>()).Returns(call =>
            {
                call[1] = "Prog";
                return true;
            });
            Workspace.TryGetModule(Arg.Is<string>(path => path != Path), out Arg.Any<string>()).Returns(false);
            Debugging.SetBreakpointsAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<SessionBreakpointLine>>(), Arg.Any<CancellationToken>())
                .Returns(call => Task.FromResult(new HostDebugBreakpointsResult
                {
                    Breakpoints = [.. call.Arg<IReadOnlyList<SessionBreakpointLine>>().Select(breakpoint => new HostBreakpoint(breakpoint.Line, true))],
                }));
            Debugging.EditStatusAsync(Arg.Any<CancellationToken>()).Returns(new HostDebugEditStatusResult());
            Workspace.ReloadChangedAsync(Arg.Any<CancellationToken>()).Returns((string?)null);
            EditStatus = new EditStatusMonitor(Debugging, Substitute.For<IWorkspaceRefresh>(), NullLogger<EditStatusMonitor>.Instance);
            Adapter = new ProgramDebugAdapter(Workspace, Debugging, Relay, EditStatus, NullLogger<ProgramDebugAdapter>.Instance);
        }

        // OmniSharp 0.19.9's client does not send supportsInvalidatedEvent, whatever its options say: a client that takes the event is one that initializes
        // the adapter saying so, as the adapter's host does with what a client sends.
        public async Task StartAsync(bool linesStartAt1 = true, bool supportsInvalidated = true)
        {
            var toServer = new Pipe();
            var toClient = new Pipe();

            // the server is started when the client has initialized it.
            var starting = DebugAdapterApp.StartServerAsync(new DebugAdapterTransport(toServer.Reader.AsStream(), toClient.Writer.AsStream()), Adapter);
            Client = await DebugAdapterClient.From(options =>
            {
                options.WithInput(toClient.Reader.AsStream()).WithOutput(toServer.Writer.AsStream());
                options.LinesStartAt1 = linesStartAt1;
                options.ColumnsStartAt1 = linesStartAt1;
                options.OnInvalidated(invalidated => _events.Enqueue(invalidated));
                options.OnStopped(stopped => _events.Enqueue(stopped));
                options.OnOutput(output => _events.Enqueue(output));
                options.OnExited(exited => _events.Enqueue(exited));
                options.OnTerminated(terminated => _events.Enqueue(terminated));
            });
            Server = await starting.WaitAsync(Patience);
            Adapter.Initialize(new InitializeRequestArguments
            {
                AdapterId = "rdcore", LinesStartAt1 = linesStartAt1, ColumnsStartAt1 = linesStartAt1, SupportsInvalidatedEvent = supportsInvalidated,
            });
        }

        public Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request)
            => Client.SendRequest(request, CancellationToken.None).WaitAsync(Patience);

        public Task LaunchAsync(string module = "Prog", string entryPoint = "Main")
            => SendAsync(new ProgramLaunchRequestArguments { Module = module, EntryPoint = entryPoint });

        public void Lose(string component) => _lost.TrySetResult(component);

        /// <summary>The next event of the type that satisfies <paramref name="where"/>, whenever it comes.</summary>
        public async Task<TEvent> NextAsync<TEvent>(Func<TEvent, bool>? where = null)
        {
            var deadline = DateTime.UtcNow + Patience;
            while (DateTime.UtcNow < deadline)
            {
                var kept = new List<object>();
                TEvent? found = default;
                var hit = false;
                while (_events.TryDequeue(out var next))
                {
                    if (!hit && next is TEvent typed && (where?.Invoke(typed) ?? true))
                    {
                        (found, hit) = (typed, true);
                    }
                    else
                    {
                        kept.Add(next);
                    }
                }

                // what was not asked for stays for who does.
                kept.ForEach(_events.Enqueue);
                if (hit)
                {
                    return found!;
                }

                await Task.Delay(10);
            }

            Assert.Fail($"no {typeof(TEvent).Name} came");
            return default!;
        }

        /// <summary>Everything that was said and not asked for yet, of one kind.</summary>
        public IReadOnlyList<TEvent> Pending<TEvent>() => [.. _events.OfType<TEvent>()];

        public async ValueTask DisposeAsync()
        {
            Client?.Dispose();
            Server?.Dispose();
            await Task.CompletedTask;
        }
    }

    private static ExecuteSessionResult Suspended(params string[] output) => new() { Outcome = ExecutionOutcome.Suspended, Output = output };

    private static HostDebugStackResult StackAt(int line, string module = "Prog", string procedure = "Main")
        => new() { Frames = [new HostStackFrame(0, procedure, module, line, 4)] };

    // ---- initialize ----

    [TestMethod]
    public async Task TheAdapter_SaysWhatItCanDo()
    {
        await using var session = new Session();
        await session.StartAsync();

        var capabilities = session.Client.ServerSettings;

        Assert.IsTrue(capabilities.SupportsConfigurationDoneRequest);
        Assert.IsTrue(capabilities.SupportsGotoTargetsRequest);
        Assert.IsTrue(capabilities.SupportsTerminateRequest);
        Assert.IsTrue(capabilities.SupportsEvaluateForHovers);
    }

    // ---- launch ----

    [TestMethod]
    public async Task ALaunch_BringsTheWorkspaceUp()
    {
        await using var session = new Session();
        await session.StartAsync();

        await session.LaunchAsync();

        await session.Workspace.Received(1).OpenAsync(Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ALaunch_ThatNamesNoModule_IsRefused()
    {
        await using var session = new Session();
        await session.StartAsync();

        var refusal = await Assert.ThrowsAsync<JsonRpcException>(() => session.SendAsync(new ProgramLaunchRequestArguments()));

        StringAssert.Contains(refusal.Message, "module");
        await session.Workspace.DidNotReceive().OpenAsync(Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ALaunch_OfAModuleThatTheWorkspaceDoesNotHave_IsRefused()
    {
        await using var session = new Session();
        await session.StartAsync();

        var refusal = await Assert.ThrowsAsync<JsonRpcException>(() => session.LaunchAsync("Nothing"));

        StringAssert.Contains(refusal.Message, "Nothing");
    }

    [TestMethod]
    public async Task ALaunch_OfAWorkspaceThatCannotBeOpened_SaysWhy()
    {
        await using var session = new Session();
        session.Workspace.OpenAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException(new InvalidOperationException("no project")));
        await session.StartAsync();

        var refusal = await Assert.ThrowsAsync<JsonRpcException>(() => session.LaunchAsync());

        StringAssert.Contains(refusal.Message, "no project");
    }

    // ---- breakpoints ----

    [TestMethod]
    public async Task ABreakpoint_IsSetInTheModuleOfTheFile_OnTheLineBeforeItsNumber()
    {
        await using var session = new Session();
        await session.StartAsync();
        await session.LaunchAsync();

        var response = await session.SendAsync(new SetBreakpointsArguments
        {
            Source = new Source { Path = Path },
            Breakpoints = new Container<SourceBreakpoint>(new SourceBreakpoint { Line = 12 }, new SourceBreakpoint { Line = 20 }),
        });

        await session.Debugging.Received(1).SetBreakpointsAsync("Prog", Arg.Is<IReadOnlyList<SessionBreakpointLine>>(bp => bp.SequenceEqual(new[] { new SessionBreakpointLine { Line = 11 }, new SessionBreakpointLine { Line = 19 } })), Arg.Any<CancellationToken>());
        CollectionAssert.AreEqual(new[] { 12, 20 }, response.Breakpoints.Select(breakpoint => breakpoint.Line!.Value).ToArray());
        Assert.IsTrue(response.Breakpoints.All(breakpoint => breakpoint.Verified));
    }

    [TestMethod]
    public async Task ABreakpoint_IsOnTheLineWithTheNumberItHas_WhenTheClientCountsFromZero()
    {
        await using var session = new Session();
        await session.StartAsync(linesStartAt1: false);
        await session.LaunchAsync();

        var response = await session.SendAsync(new SetBreakpointsArguments
        {
            Source = new Source { Path = Path },
            Breakpoints = new Container<SourceBreakpoint>(new SourceBreakpoint { Line = 11 }),
        });

        await session.Debugging.Received(1).SetBreakpointsAsync("Prog", Arg.Is<IReadOnlyList<SessionBreakpointLine>>(bp => bp.SequenceEqual(new[] { new SessionBreakpointLine { Line = 11 } })), Arg.Any<CancellationToken>());
        Assert.AreEqual(11, response.Breakpoints.Single().Line);
    }

    [TestMethod]
    public async Task ABreakpoint_CarriesItsCondition_ToTheModule()
    {
        await using var session = new Session();
        await session.StartAsync();
        await session.LaunchAsync();

        _ = await session.SendAsync(new SetBreakpointsArguments
        {
            Source = new Source { Path = Path },
            Breakpoints = new Container<SourceBreakpoint>(new SourceBreakpoint { Line = 12, Condition = "i > 5" }),
        });

        await session.Debugging.Received(1).SetBreakpointsAsync("Prog", Arg.Is<IReadOnlyList<SessionBreakpointLine>>(bp => bp.SequenceEqual(new[] { new SessionBreakpointLine { Line = 11, Condition = "i > 5" } })), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ABreakpoint_WhoseConditionIsRefused_IsNotVerified_AndSaysWhy()
    {
        await using var session = new Session();
        session.Debugging.SetBreakpointsAsync("Prog", Arg.Any<IReadOnlyList<SessionBreakpointLine>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HostDebugBreakpointsResult { Breakpoints = [new HostBreakpoint(11, true, "expected an expression")], Judged = true }));
        await session.StartAsync();
        await session.LaunchAsync();

        var response = await session.SendAsync(new SetBreakpointsArguments
        {
            Source = new Source { Path = Path },
            Breakpoints = new Container<SourceBreakpoint>(new SourceBreakpoint { Line = 12, Condition = "i >" }),
        });

        Assert.IsFalse(response.Breakpoints.Single().Verified, "it is never waited at");
        Assert.AreEqual("expected an expression", response.Breakpoints.Single().Message);
    }

    [TestMethod]
    public async Task ABreakpoint_OnALineThatNoStatementBeginsOn_IsNotVerified()
    {
        await using var session = new Session();
        session.Debugging.SetBreakpointsAsync("Prog", Arg.Any<IReadOnlyList<SessionBreakpointLine>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HostDebugBreakpointsResult { Breakpoints = [new HostBreakpoint(3, false)] }));
        await session.StartAsync();
        await session.LaunchAsync();

        var response = await session.SendAsync(new SetBreakpointsArguments
        {
            Source = new Source { Path = Path },
            Breakpoints = new Container<SourceBreakpoint>(new SourceBreakpoint { Line = 4 }),
        });

        Assert.IsFalse(response.Breakpoints.Single().Verified);
        Assert.AreEqual(4, response.Breakpoints.Single().Line);
    }

    [TestMethod]
    public async Task ABreakpoint_InAFileThatIsNotInTheWorkspace_IsNotVerified_AndNothingIsAsked()
    {
        await using var session = new Session();
        await session.StartAsync();
        await session.LaunchAsync();

        var response = await session.SendAsync(new SetBreakpointsArguments
        {
            Source = new Source { Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "elsewhere.bas") },
            Breakpoints = new Container<SourceBreakpoint>(new SourceBreakpoint { Line = 1 }),
        });

        Assert.IsFalse(response.Breakpoints.Single().Verified);
        await session.Debugging.DidNotReceiveWithAnyArgs().SetBreakpointsAsync(default!, default!, default);
    }

    [TestMethod]
    public async Task Breakpoints_SetBeforeTheWorkspaceIsUp_WaitForIt()
    {
        await using var session = new Session();
        var opening = new TaskCompletionSource();
        var opened = new TaskCompletionSource();
        session.Workspace.OpenAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            opened.TrySetResult();
            return opening.Task;
        });
        await session.StartAsync();

        var launching = session.LaunchAsync();
        var setting = session.SendAsync(new SetBreakpointsArguments
        {
            Source = new Source { Path = Path },
            Breakpoints = new Container<SourceBreakpoint>(new SourceBreakpoint { Line = 2 }),
        });
        await opened.Task.WaitAsync(Patience);
        Assert.IsFalse(setting.IsCompleted, "the workspace is being opened, and there is none to set a breakpoint in yet");

        opening.SetResult();
        await launching;
        var response = await setting;

        Assert.IsTrue(response.Breakpoints.Single().Verified);
    }

    // ---- running ----

    [TestMethod]
    public async Task TheProgram_IsStartedWhenTheClientIsDoneConfiguring_AndTheClientIsAnsweredAtOnce()
    {
        await using var session = new Session();
        var running = new TaskCompletionSource<ExecuteSessionResult>();
        var started = new TaskCompletionSource();
        session.Workspace.StartAsync("Prog", "Main", Arg.Any<CancellationToken>()).Returns(_ =>
        {
            started.TrySetResult();
            return running.Task;
        });
        await session.StartAsync();
        await session.LaunchAsync();

        _ = await session.SendAsync(new ConfigurationDoneArguments());

        await started.Task.WaitAsync(Patience);
        running.SetResult(new ExecuteSessionResult { Outcome = ExecutionOutcome.Completed });
        _ = await session.NextAsync<TerminatedEvent>();
    }

    [TestMethod]
    public async Task TheProgram_IsNotStartedBeforeALaunch()
    {
        await using var session = new Session();
        await session.StartAsync();

        _ = await Assert.ThrowsAsync<JsonRpcException>(() => session.SendAsync(new ConfigurationDoneArguments()));
    }

    [TestMethod]
    public async Task AProgramThatWaitsAtABreakpoint_IsSaidToHaveStoppedThere()
    {
        await using var session = new Session();
        session.Workspace.StartAsync(default!, default!, default).ReturnsForAnyArgs(Task.FromResult(Suspended("start")));
        session.Debugging.StackAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(StackAt(line: 11)));
        await session.StartAsync();
        await session.LaunchAsync();
        _ = await session.SendAsync(new SetBreakpointsArguments { Source = new Source { Path = Path }, Breakpoints = new Container<SourceBreakpoint>(new SourceBreakpoint { Line = 12 }) });

        _ = await session.SendAsync(new ConfigurationDoneArguments());

        var output = await session.NextAsync<OutputEvent>();
        var stopped = await session.NextAsync<StoppedEvent>();
        Assert.AreEqual("start\n", output.Output);
        Assert.AreEqual(OutputEventCategory.StandardOutput, output.Category);
        Assert.AreEqual(StoppedEventReason.Breakpoint, stopped.Reason);
        Assert.AreEqual(1, stopped.ThreadId);
        Assert.IsTrue(stopped.AllThreadsStopped);
    }

    [TestMethod]
    public async Task AProgramThatWaitsAtABreakpointWhoseConditionFailed_IsSaidToHaveStoppedThere_AndWhy()
    {
        await using var session = new Session();
        session.Workspace.StartAsync(default!, default!, default).ReturnsForAnyArgs(Task.FromResult(Suspended() with { ConditionError = "Division by zero" }));
        await session.StartAsync();
        await session.LaunchAsync();

        _ = await session.SendAsync(new ConfigurationDoneArguments());

        var stopped = await session.NextAsync<StoppedEvent>();
        Assert.AreEqual(StoppedEventReason.Breakpoint, stopped.Reason, "it is a breakpoint, and not an error of the program's");
        Assert.AreEqual(string.Format(DebuggerMessages.StopAtBreakpointConditionFailed, "Division by zero"), stopped.Text);
    }

    [TestMethod]
    public async Task AProgramThatWaitsAtAStatementNoBreakpointIsOn_IsSaidToHaveStoppedAtAStopStatement()
    {
        await using var session = new Session();
        session.Workspace.StartAsync(default!, default!, default).ReturnsForAnyArgs(Task.FromResult(Suspended()));
        session.Debugging.StackAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(StackAt(line: 12)));
        await session.StartAsync();
        await session.LaunchAsync();

        _ = await session.SendAsync(new ConfigurationDoneArguments());

        var stopped = await session.NextAsync<StoppedEvent>();
        StringAssert.Contains(stopped.Description, "Stop");
    }

    [TestMethod]
    public async Task WhatTheProgramPrints_IsSaidAsItPrints_AndBeforeTheProgramIsSaidToHaveStopped()
    {
        await using var session = new Session();
        session.Workspace.StartAsync(default!, default!, default).ReturnsForAnyArgs(Task.FromResult(
            new ExecuteSessionResult { Outcome = ExecutionOutcome.Suspended, StreamedLines = 2 }));
        session.Debugging.StackAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(StackAt(line: 12)));
        await session.StartAsync();
        await session.LaunchAsync();

        _ = await session.SendAsync(new ConfigurationDoneArguments());
        session.Relay.Publish(new HostOutputNotification { Lines = ["first"], Total = 1 });

        var first = await session.NextAsync<OutputEvent>();
        Assert.AreEqual("first\n", first.Output);
        Assert.AreEqual(0, session.Pending<StoppedEvent>().Count, "the second line the program printed is not here yet, and it printed it before it stopped");

        session.Relay.Publish(new HostOutputNotification { Lines = ["second"], Total = 2 });

        Assert.AreEqual("second\n", (await session.NextAsync<OutputEvent>()).Output);
        _ = await session.NextAsync<StoppedEvent>();
    }

    [TestMethod]
    [DataRow(StepKind.Over, "next")]
    [DataRow(StepKind.Into, "stepIn")]
    [DataRow(StepKind.Out, "stepOut")]
    public async Task AStep_ResumesTheProgramByThatMuch_AndTheProgramWaitsAgainWithAReasonOfStep(StepKind step, string request)
    {
        await using var session = new Session();
        await StartedAndWaitingAsync(session);
        session.Debugging.ResumeAsync(step, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Suspended()));

        _ = request switch
        {
            "next" => await session.SendAsync(new NextArguments { ThreadId = 1 }),
            "stepIn" => (object)await session.SendAsync(new StepInArguments { ThreadId = 1 }),
            _ => await session.SendAsync(new StepOutArguments { ThreadId = 1 }),
        };

        var stopped = await session.NextAsync<StoppedEvent>();
        Assert.AreEqual(StoppedEventReason.Step, stopped.Reason);
        await session.Debugging.Received(1).ResumeAsync(step, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task AContinue_ResumesTheProgramToWhereverItGoes()
    {
        await using var session = new Session();
        await StartedAndWaitingAsync(session);
        session.Debugging.ResumeAsync(null, Arg.Any<CancellationToken>()).Returns(Task.FromResult(new ExecuteSessionResult { Outcome = ExecutionOutcome.Completed, Output = ["done"] }));

        var response = await session.SendAsync(new ContinueArguments { ThreadId = 1 });

        Assert.IsTrue(response.AllThreadsContinued);
        Assert.AreEqual("done\n", (await session.NextAsync<OutputEvent>()).Output);
        Assert.AreEqual(0, (await session.NextAsync<ExitedEvent>()).ExitCode);
        _ = await session.NextAsync<TerminatedEvent>();
    }

    [TestMethod]
    public async Task TheAdapter_OffersToWaitAtUnhandledErrors_OrAtAllOfThem()
    {
        await using var session = new Session();
        await session.StartAsync();

        var filters = session.Client.ServerSettings.ExceptionBreakpointFilters!.ToArray();

        CollectionAssert.AreEquivalent(new[] { "unhandled", "all" }, filters.Select(filter => filter.Filter).ToArray());
        Assert.IsTrue(filters.Single(filter => filter.Filter == "unhandled").Default);
        Assert.IsTrue(session.Client.ServerSettings.SupportsExceptionInfoRequest);
    }

    [TestMethod]
    [DataRow("all", ErrorBreakMode.All)]
    [DataRow("unhandled", ErrorBreakMode.Unhandled)]
    public async Task TheErrorsTheClientAsksToWaitAt_AreTheErrorsTheProgramWaitsAt(string filter, ErrorBreakMode mode)
    {
        await using var session = new Session();
        await session.StartAsync();
        await session.LaunchAsync();

        _ = await session.SendAsync(new SetExceptionBreakpointsArguments { Filters = new Container<string>(filter) });

        await session.Debugging.Received(1).SetErrorBreakAsync(mode, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task NoFilter_IsNoErrorsToWaitAt()
    {
        await using var session = new Session();
        await session.StartAsync();
        await session.LaunchAsync();

        _ = await session.SendAsync(new SetExceptionBreakpointsArguments { Filters = new Container<string>() });

        await session.Debugging.Received(1).SetErrorBreakAsync(ErrorBreakMode.None, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task AProgramThatWaitsAtAnError_IsSaidToHaveStoppedOnAnException_AndTheClientCanAskWhich()
    {
        await using var session = new Session();
        session.Workspace.StartAsync(default!, default!, default).ReturnsForAnyArgs(Task.FromResult(new ExecuteSessionResult
        {
            Outcome = ExecutionOutcome.Suspended,
            ErrorNumber = 11,
            ErrorMessage = "Division by zero",
            ErrorCode = "VBR00011",
            ErrorTitle = "Run-time error",
        }));
        await session.StartAsync();
        await session.LaunchAsync();
        _ = await session.SendAsync(new SetExceptionBreakpointsArguments { Filters = new Container<string>("all") });

        _ = await session.SendAsync(new ConfigurationDoneArguments());

        var stopped = await session.NextAsync<StoppedEvent>();
        Assert.AreEqual(StoppedEventReason.Exception, stopped.Reason);
        StringAssert.Contains(stopped.Description, "Division by zero");
        var info = await session.SendAsync(new ExceptionInfoArguments { ThreadId = 1 });
        Assert.AreEqual("VBR00011", info.ExceptionId);
        Assert.AreEqual("Division by zero", info.Description);
        Assert.AreEqual(ExceptionBreakMode.Always, info.BreakMode);
    }

    [TestMethod]
    public async Task ThereIsNoExceptionToAskAbout_WhenTheProgramDoesNotWaitAtAnError()
    {
        await using var session = new Session();
        await StartedAndWaitingAsync(session);

        var refusal = await Assert.ThrowsAsync<JsonRpcException>(() => session.SendAsync(new ExceptionInfoArguments { ThreadId = 1 }));

        StringAssert.Contains(refusal.Message, "error");
    }

    [TestMethod]
    public async Task APause_AsksTheProgramToStop_AndTheProgramWaitsWithAReasonOfPause()
    {
        await using var session = new Session();
        await StartedAndWaitingAsync(session);
        var running = new TaskCompletionSource<ExecuteSessionResult>();
        session.Debugging.ResumeAsync(null, Arg.Any<CancellationToken>()).Returns(running.Task);
        session.Debugging.PauseAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(new HostDebugAck { Acted = true }));
        _ = await session.SendAsync(new ContinueArguments { ThreadId = 1 });

        _ = await session.SendAsync(new PauseArguments { ThreadId = 1 });
        running.SetResult(Suspended());

        var stopped = await session.NextAsync<StoppedEvent>(e => e.Reason == StoppedEventReason.Pause);
        Assert.AreEqual(StoppedEventReason.Pause, stopped.Reason);
        await session.Debugging.Received(1).PauseAsync(Arg.Any<CancellationToken>());
    }

    // a session whose program was started and waits at a Stop; the first stop is consumed.
    private static async Task StartedAndWaitingAsync(Session session)
    {
        session.Workspace.StartAsync(default!, default!, default).ReturnsForAnyArgs(Task.FromResult(Suspended()));
        session.Debugging.StackAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(StackAt(line: 12)));
        await session.StartAsync();
        await session.LaunchAsync();
        _ = await session.SendAsync(new ConfigurationDoneArguments());
        _ = await session.NextAsync<StoppedEvent>();
    }

    // ---- the end of the program ----

    [TestMethod]
    public async Task AProgramThatRaisesAnError_IsSaidToHaveEnded_WithTheErrorWhereItWas()
    {
        await using var session = new Session();
        session.Workspace.StartAsync(default!, default!, default).ReturnsForAnyArgs(Task.FromResult(new ExecuteSessionResult
        {
            Outcome = ExecutionOutcome.RuntimeError,
            ErrorNumber = 11,
            ErrorMessage = "Division by zero",
            ErrorTitle = "Run-time error",
            ErrorLine = 4,
        }));
        await session.StartAsync();
        await session.LaunchAsync();
        _ = await session.SendAsync(new SetBreakpointsArguments { Source = new Source { Path = Path }, Breakpoints = new Container<SourceBreakpoint>() });

        _ = await session.SendAsync(new ConfigurationDoneArguments());

        var output = await session.NextAsync<OutputEvent>();
        StringAssert.Contains(output.Output, "Run-time error '11': Division by zero");
        Assert.AreEqual(OutputEventCategory.StandardError, output.Category);
        Assert.AreEqual(5, output.Line, "the line of the error, as the client numbers them");
        Assert.AreEqual(Path, output.Source!.Path);
        Assert.AreEqual(1, (await session.NextAsync<ExitedEvent>()).ExitCode);
        _ = await session.NextAsync<TerminatedEvent>();
    }

    [TestMethod]
    public async Task AProgramThatIsOver_IsSaidToBeOverOnce()
    {
        await using var session = new Session();
        await StartedAndWaitingAsync(session);
        session.Debugging.ResumeAsync(null, Arg.Any<CancellationToken>()).Returns(Task.FromResult(new ExecuteSessionResult { Outcome = ExecutionOutcome.Completed }));
        session.Debugging.TerminateAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(new HostDebugAck()));

        _ = await session.SendAsync(new ContinueArguments { ThreadId = 1 });
        _ = await session.NextAsync<TerminatedEvent>();
        _ = await session.SendAsync(new TerminateArguments());

        // what the adapter sent before it answered a request it answers at once has come.
        _ = await session.SendAsync(new ThreadsArguments());
        Assert.AreEqual(0, session.Pending<TerminatedEvent>().Count);
        Assert.AreEqual(0, session.Pending<ExitedEvent>().Count - 1, "the one exit that was not asked for yet");
    }

    [TestMethod]
    public async Task ARequestTheProgramCannotBeAskedNow_DoesNotEndIt()
    {
        await using var session = new Session();
        await StartedAndWaitingAsync(session);
        session.Debugging.ResumeAsync(null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ExecuteSessionResult { Outcome = ExecutionOutcome.Refused, ErrorMessage = "the program is running" }));

        _ = await session.SendAsync(new ContinueArguments { ThreadId = 1 });

        var output = await session.NextAsync<OutputEvent>();
        StringAssert.Contains(output.Output, "the program is running");

        // what the adapter sent before it answered a request it answers at once has come.
        _ = await session.SendAsync(new ThreadsArguments());
        Assert.AreEqual(0, session.Pending<TerminatedEvent>().Count);
    }

    [TestMethod]
    public async Task AStatementTypedInTheDebugConsole_IsRunInTheFrame_AndIsNotEvaluated()
    {
        await using var session = new Session();
        await StartedAndWaitingAsync(session);
        session.Debugging.ExecuteAsync(1, "k = 5", Arg.Any<CancellationToken>()).Returns(Task.FromResult(new HostDebugEvaluateResult { Success = true }));

        var response = await session.SendAsync(new EvaluateArguments { Expression = "k = 5", FrameId = 1, Context = EvaluateArgumentsContext.Repl });

        Assert.AreEqual(string.Empty, response.Result);
        await session.Debugging.DidNotReceiveWithAnyArgs().EvaluateAsync(default, default!, default);
    }

    [TestMethod]
    public async Task AStatementThatFails_IsRefused_WithTheReason()
    {
        await using var session = new Session();
        await StartedAndWaitingAsync(session);
        session.Debugging.ExecuteAsync(0, "k = 1 \\ 0", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HostDebugEvaluateResult { Error = "Division by zero" }));

        var refusal = await Assert.ThrowsAsync<JsonRpcException>(() => session.SendAsync(new EvaluateArguments { Expression = "k = 1 \\ 0", FrameId = 0, Context = EvaluateArgumentsContext.Repl }));

        StringAssert.Contains(refusal.Message, "Division by zero");
    }

    [TestMethod]
    public async Task WhatTheDebugConsoleShows_IsWhatTheStatementPrinted_AndNothingElse()
    {
        await using var session = new Session();
        await StartedAndWaitingAsync(session);
        session.Debugging.ExecuteAsync(0, "?k + 1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HostDebugEvaluateResult { Success = true, Output = [" 8 "] }));

        var response = await session.SendAsync(new EvaluateArguments { Expression = "?k + 1", FrameId = 0, Context = EvaluateArgumentsContext.Repl });

        var output = await session.NextAsync<OutputEvent>();
        Assert.AreEqual(" 8 \n", output.Output);
        Assert.AreEqual(OutputEventCategory.Console, output.Category);
        Assert.AreEqual(string.Empty, response.Result, "a value is shown by printing it");
    }

    [TestMethod]
    [DataRow("watch")]
    [DataRow("hover")]
    public async Task AWatchOrAHover_IsOnlyEverEvaluated_NeverRunAsAStatement(string kind)
    {
        var context = kind == "watch" ? EvaluateArgumentsContext.Watch : EvaluateArgumentsContext.Hover;
        await using var session = new Session();
        await StartedAndWaitingAsync(session);
        session.Debugging.EvaluateAsync(0, "k = 5", Arg.Any<CancellationToken>()).Returns(Task.FromResult(new HostDebugEvaluateResult { Success = true, Value = "False", Type = "Boolean" }));

        var response = await session.SendAsync(new EvaluateArguments { Expression = "k = 5", FrameId = 0, Context = context });

        Assert.AreEqual("False", response.Result);
        await session.Debugging.DidNotReceiveWithAnyArgs().ExecuteAsync(default, default!, default);
    }

    [TestMethod]
    public async Task ATerminate_EndsTheProgram_AndSaysSo()
    {
        await using var session = new Session();
        await StartedAndWaitingAsync(session);
        session.Debugging.TerminateAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(new HostDebugAck { Acted = true }));

        _ = await session.SendAsync(new TerminateArguments());

        await session.Debugging.Received(1).TerminateAsync(true, Arg.Any<CancellationToken>());
        _ = await session.NextAsync<ExitedEvent>();
        _ = await session.NextAsync<TerminatedEvent>();
    }

    [TestMethod]
    public async Task ADisconnect_EndsTheProgramTheClientStarted_AndTheAdapterIsDone()
    {
        await using var session = new Session();
        await StartedAndWaitingAsync(session);
        session.Debugging.TerminateAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(new HostDebugAck { Acted = true }));

        _ = await session.SendAsync(new DisconnectArguments { TerminateDebuggee = true });

        await session.Debugging.Received(1).TerminateAsync(true, Arg.Any<CancellationToken>());
        await session.Adapter.Closed.WaitAsync(Patience);
    }

    [TestMethod]
    public async Task AComponentTheDebuggingCannotGoOnWithout_WhenLost_EndsTheProgram()
    {
        await using var session = new Session();
        await StartedAndWaitingAsync(session);

        await session.Adapter.LostAsync("environment host");

        var output = await session.NextAsync<OutputEvent>();
        StringAssert.Contains(output.Output, "environment host");
        Assert.AreEqual(1, (await session.NextAsync<ExitedEvent>()).ExitCode);
        _ = await session.NextAsync<TerminatedEvent>();
    }

    // ---- where the program is ----

    [TestMethod]
    public async Task TheProgram_HasOneThread()
    {
        await using var session = new Session();
        await session.StartAsync();

        var threads = await session.SendAsync(new ThreadsArguments());

        Assert.AreEqual(1, threads.Threads.Single().Id);
    }

    [TestMethod]
    public async Task TheStack_IsTheActivationsWithTheirFilesAndTheirPlacesCountedFromOne()
    {
        await using var session = new Session();
        await StartedAndWaitingAsync(session);
        session.Debugging.StackAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(new HostDebugStackResult
        {
            Frames =
            [
                new HostStackFrame(0, "Helper", "Prog", 11, 4, Handler: "ErrHandler"),
                new HostStackFrame(1, "Main", "Prog", 6, 4),
            ],
        }));

        var stack = await session.SendAsync(new StackTraceArguments { ThreadId = 1 });

        Assert.AreEqual(2, stack.TotalFrames);
        var innermost = stack.StackFrames!.First();
        Assert.AreEqual("Helper (in ErrHandler)", innermost.Name);
        Assert.AreEqual(12, innermost.Line);
        Assert.AreEqual(5, innermost.Column);
        Assert.AreEqual(Path, innermost.Source!.Path);
        Assert.AreEqual("Prog.bas", innermost.Source.Name);
        Assert.AreEqual("Main", stack.StackFrames!.Last().Name);
    }

    [TestMethod]
    public async Task AGoSubThatWasNotReturnedFrom_IsAFrameOfItsOwn_WithTheVariablesOfTheActivation()
    {
        await using var session = new Session();
        await StartedAndWaitingAsync(session);
        session.Debugging.StackAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(new HostDebugStackResult
        {
            Frames =
            [
                new HostStackFrame(0, "Helper", "Prog", 20, 4, ReturnLines: [14, 6]),
                new HostStackFrame(1, "Main", "Prog", 3, 4),
            ],
        }));
        session.Debugging.VariablesAsync(0, HostVariableScope.Locals, 0, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HostDebugVariablesResult { Variables = [new HostVariable("k", "7", "Long")] }));
        session.Debugging.EvaluateAsync(0, "k", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HostDebugEvaluateResult { Success = true, Value = "7", Type = "Long" }));

        var stack = await session.SendAsync(new StackTraceArguments { ThreadId = 1 });

        var frames = stack.StackFrames!.ToArray();
        CollectionAssert.AreEqual(new[] { "Helper", "Helper (GoSub)", "Helper (GoSub)", "Main" }, frames.Select(frame => frame.Name).ToArray());
        CollectionAssert.AreEqual(new[] { 21, 15, 7, 4 }, frames.Select(frame => frame.Line).ToArray(), "the line the GoSub is on, innermost first");
        Assert.AreEqual(4, stack.TotalFrames);
        Assert.AreEqual(4, frames.Select(frame => frame.Id).Distinct().Count());

        var scopes = await session.SendAsync(new ScopesArguments { FrameId = frames[2].Id });
        var locals = await session.SendAsync(new VariablesArguments { VariablesReference = scopes.Scopes.First().VariablesReference });
        var evaluated = await session.SendAsync(new EvaluateArguments { Expression = "k", FrameId = frames[1].Id });

        Assert.AreEqual("7", locals.Variables.Single().Value);
        Assert.AreEqual("7", evaluated.Result);
    }

    [TestMethod]
    public async Task TheStack_CanBeAskedForAPartOfIt()
    {
        await using var session = new Session();
        await StartedAndWaitingAsync(session);
        session.Debugging.StackAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(new HostDebugStackResult
        {
            Frames = [new HostStackFrame(0, "C", "Prog", 1, 0), new HostStackFrame(1, "B", "Prog", 1, 0), new HostStackFrame(2, "A", "Prog", 1, 0)],
        }));

        var stack = await session.SendAsync(new StackTraceArguments { ThreadId = 1, StartFrame = 1, Levels = 1 });

        Assert.AreEqual("B", stack.StackFrames!.Single().Name);
        Assert.AreEqual(3, stack.TotalFrames);
    }

    [TestMethod]
    public async Task AFrame_HasLocalsAndModuleVariables_InOrder()
    {
        await using var session = new Session();
        await StartedAndWaitingAsync(session);
        session.Debugging.VariablesAsync(1, HostVariableScope.Locals, 0, Arg.Any<CancellationToken>()).Returns(Task.FromResult(new HostDebugVariablesResult
        {
            Variables = [new HostVariable("k", "7", "Long"), new HostVariable("arr", "", "Long(1 To 2)", Reference: 5)],
        }));

        var scopes = await session.SendAsync(new ScopesArguments { FrameId = 1 });
        var locals = await session.SendAsync(new VariablesArguments { VariablesReference = scopes.Scopes.First().VariablesReference });

        CollectionAssert.AreEqual(new[] { "Locals", "Module" }, scopes.Scopes.Select(scope => scope.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "k", "arr" }, locals.Variables.Select(variable => variable.Name).ToArray());
        Assert.AreEqual("7", locals.Variables.First().Value);
        Assert.AreEqual(0, locals.Variables.First().VariablesReference);
        Assert.AreNotEqual(0, locals.Variables.Last().VariablesReference, "an array has parts to ask for");
    }

    [TestMethod]
    public async Task ThePartsOfAnArray_AreAskedForThroughTheReferenceItWasGiven()
    {
        await using var session = new Session();
        await StartedAndWaitingAsync(session);
        session.Debugging.VariablesAsync(0, HostVariableScope.Locals, 0, Arg.Any<CancellationToken>()).Returns(Task.FromResult(new HostDebugVariablesResult
        {
            Variables = [new HostVariable("arr", "", "Long(1 To 2)", Reference: 5)],
        }));
        session.Debugging.VariablesAsync(0, HostVariableScope.Locals, 5, Arg.Any<CancellationToken>()).Returns(Task.FromResult(new HostDebugVariablesResult
        {
            Variables = [new HostVariable("(1)", "9", "Long"), new HostVariable("(2)", "8", "Long")],
        }));
        var scopes = await session.SendAsync(new ScopesArguments { FrameId = 0 });
        var locals = await session.SendAsync(new VariablesArguments { VariablesReference = scopes.Scopes.First().VariablesReference });

        var parts = await session.SendAsync(new VariablesArguments { VariablesReference = locals.Variables.Single().VariablesReference });

        CollectionAssert.AreEqual(new[] { "9", "8" }, parts.Variables.Select(part => part.Value).ToArray());
    }

    [TestMethod]
    public async Task AReference_IsNothingOnceTheProgramWentOn()
    {
        await using var session = new Session();
        await StartedAndWaitingAsync(session);
        session.Debugging.ResumeAsync(Arg.Any<StepKind?>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(Suspended()));
        var scopes = await session.SendAsync(new ScopesArguments { FrameId = 0 });
        var before = scopes.Scopes.First().VariablesReference;

        _ = await session.SendAsync(new NextArguments { ThreadId = 1 });
        _ = await session.NextAsync<StoppedEvent>();
        var after = await session.SendAsync(new VariablesArguments { VariablesReference = before });

        Assert.AreEqual(0, after.Variables.Count());
        await session.Debugging.DidNotReceiveWithAnyArgs().VariablesAsync(default, default, default, default);
    }

    // ---- evaluate ----

    [TestMethod]
    public async Task AnExpression_IsEvaluatedInTheFrameTheClientSays()
    {
        await using var session = new Session();
        await StartedAndWaitingAsync(session);
        session.Debugging.EvaluateAsync(1, "k + 1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HostDebugEvaluateResult { Success = true, Value = "8", Type = "Long" }));

        var response = await session.SendAsync(new EvaluateArguments { Expression = "k + 1", FrameId = 1, Context = EvaluateArgumentsContext.Watch });

        Assert.AreEqual("8", response.Result);
        Assert.AreEqual("Long", response.Type);
        Assert.AreEqual(0, response.VariablesReference);
    }

    [TestMethod]
    public async Task AnExpression_WithNoFrame_IsEvaluatedInTheInnermost()
    {
        await using var session = new Session();
        await StartedAndWaitingAsync(session);
        session.Debugging.EvaluateAsync(0, "1", Arg.Any<CancellationToken>()).Returns(Task.FromResult(new HostDebugEvaluateResult { Success = true, Value = "1", Type = "Integer" }));

        _ = await session.SendAsync(new EvaluateArguments { Expression = "1", Context = EvaluateArgumentsContext.Watch });

        await session.Debugging.Received(1).EvaluateAsync(0, "1", Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task AnExpression_ThatHasNoValue_IsRefused_WithTheReason()
    {
        await using var session = new Session();
        await StartedAndWaitingAsync(session);
        session.Debugging.EvaluateAsync(0, "Nope", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HostDebugEvaluateResult { Error = "'Nope' is not defined" }));

        var refusal = await Assert.ThrowsAsync<JsonRpcException>(() => session.SendAsync(new EvaluateArguments { Expression = "Nope", FrameId = 0 }));

        StringAssert.Contains(refusal.Message, "'Nope' is not defined");
    }

    [TestMethod]
    public async Task AnExpression_ThatPrints_HasItsOutputInTheDebugConsole()
    {
        await using var session = new Session();
        await StartedAndWaitingAsync(session);
        session.Debugging.EvaluateAsync(0, "Noisy()", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HostDebugEvaluateResult { Success = true, Value = "1", Type = "Long", Output = ["from the call"] }));

        _ = await session.SendAsync(new EvaluateArguments { Expression = "Noisy()", FrameId = 0, Context = EvaluateArgumentsContext.Watch });

        var output = await session.NextAsync<OutputEvent>();
        Assert.AreEqual("from the call\n", output.Output);
        Assert.AreEqual(OutputEventCategory.Console, output.Category);
    }

    [TestMethod]
    public async Task AnExpressionThatIsAnArray_HasPartsToAskFor()
    {
        await using var session = new Session();
        await StartedAndWaitingAsync(session);
        session.Debugging.EvaluateAsync(0, "arr", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HostDebugEvaluateResult { Success = true, Type = "Long(1 To 2)", Reference = 4 }));
        session.Debugging.VariablesAsync(0, Arg.Any<HostVariableScope>(), 4, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HostDebugVariablesResult { Variables = [new HostVariable("(1)", "5", "Long")] }));

        var response = await session.SendAsync(new EvaluateArguments { Expression = "arr", FrameId = 0 });
        var parts = await session.SendAsync(new VariablesArguments { VariablesReference = response.VariablesReference });

        Assert.AreNotEqual(0, response.VariablesReference);
        Assert.AreEqual("5", parts.Variables.Single().Value);
    }

    // ---- the point the program goes on from ----

    [TestMethod]
    public async Task ALineIsATarget_ToGoOnFrom()
    {
        await using var session = new Session();
        await session.StartAsync();

        var targets = await session.SendAsync(new GotoTargetsArguments { Source = new Source { Path = Path }, Line = 14 });

        Assert.AreEqual(14, targets.Targets.Single().Line);
    }

    [TestMethod]
    public async Task AGoto_MovesThePoint_AndTheProgramStillWaits()
    {
        await using var session = new Session();
        await StartedAndWaitingAsync(session);
        session.Debugging.GotoAsync(13, null, Arg.Any<CancellationToken>()).Returns(Task.FromResult(new HostDebugGotoResult { Moved = true, Line = 13 }));
        var targets = await session.SendAsync(new GotoTargetsArguments { Source = new Source { Path = Path }, Line = 14 });

        _ = await session.SendAsync(new GotoArguments { ThreadId = 1, TargetId = targets.Targets.Single().Id });

        var stopped = await session.NextAsync<StoppedEvent>();
        Assert.AreEqual(StoppedEventReason.Goto, stopped.Reason);
        await session.Debugging.DidNotReceiveWithAnyArgs().ResumeAsync(default, default);
    }

    [TestMethod]
    public async Task AGoto_ThatCannotBeDone_IsRefused_WithTheReason()
    {
        await using var session = new Session();
        await StartedAndWaitingAsync(session);
        session.Debugging.GotoAsync(Arg.Any<int>(), null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HostDebugGotoResult { Moved = false, Reason = "that line is in another procedure" }));

        var refusal = await Assert.ThrowsAsync<JsonRpcException>(() => session.SendAsync(new GotoArguments { ThreadId = 1, TargetId = 3 }));

        StringAssert.Contains(refusal.Message, "another procedure");
    }

    // ---- edit and continue ----


    private static readonly HostDebugEdit CalleeLocalRemoved = new()
    {
        Name = "Prog.Callee",
        Kind = HostDebugEditKind.Restart,
        Reason = "a local was removed",
        ModuleUri = new Uri("file:///c:/dap-ws#Prog"),
        Range = new RDCore.SDK.Model.Source.SourceRange(new(3, 0), new(5, 7)),
    };

    private static void Listing(Session session, params HostDebugEdit[] edits)
        => session.Debugging.EditStatusAsync(Arg.Any<CancellationToken>()).Returns(new HostDebugEditStatusResult { Suspended = true, Edits = [.. edits] });

    // what the adapter wrote before it answered a request it answers at once has come.
    private static async Task FlushedAsync(Session session) => _ = await session.SendAsync(new ThreadsArguments());

    // the edit is said as soon as it is loaded, which is the closest to the edit the client can be told: once, and not again while it is still listed.
    [TestMethod]
    public async Task AnEditTheProgramThatWaitsDoesNotHave_IsSaidOnce_AsSoonAsItIsLoaded()
    {
        await using var session = new Session();
        await StartedAndWaitingAsync(session);
        Listing(session, CalleeLocalRemoved);

        await session.EditStatus.CheckAsync(CancellationToken.None, afterLoad: true);
        var said = await session.NextAsync<OutputEvent>(output => output.Output.Contains("Prog.Callee", StringComparison.Ordinal));
        await session.EditStatus.CheckAsync(CancellationToken.None, afterLoad: true);
        await FlushedAsync(session);

        Assert.AreEqual(OutputEventCategory.Console, said.Category);
        StringAssert.Contains(said.Output, "a local was removed");
        Assert.IsFalse(session.Pending<OutputEvent>().Any(output => output.Output.Contains("Prog.Callee", StringComparison.Ordinal)), "said once");
    }

    // a program that waits with an edit it does not have is not asked to go on: the client is refused, with the edit, and told it too.
    [TestMethod]
    [DataRow("continue")]
    [DataRow("next")]
    [DataRow("stepIn")]
    [DataRow("stepOut")]
    public async Task GoingOnWithAnEditTheProgramDoesNotHave_IsRefused_WithTheEdit(string request)
    {
        await using var session = new Session();
        await StartedAndWaitingAsync(session);
        Listing(session, CalleeLocalRemoved);

        var refusal = await Assert.ThrowsAsync<JsonRpcException>(() => request switch
        {
            "continue" => session.SendAsync(new ContinueArguments { ThreadId = 1 }),
            "next" => session.SendAsync(new NextArguments { ThreadId = 1 }),
            "stepIn" => session.SendAsync(new StepInArguments { ThreadId = 1 }),
            _ => session.SendAsync(new StepOutArguments { ThreadId = 1 }),
        });

        StringAssert.Contains(refusal.Message, "Prog.Callee: a local was removed");
        var said = await session.NextAsync<OutputEvent>(output => output.Category == OutputEventCategory.StandardError);
        StringAssert.Contains(said.Output, "Prog.Callee");
        await session.Debugging.DidNotReceiveWithAnyArgs().ResumeAsync(default, default);
    }

    private static readonly HostDebugEdit CallerDoesNotCompile = new()
    {
        Name = "Prog.Caller",
        Kind = HostDebugEditKind.DoesNotCompile,
        Reason = "Variable not defined",
        ModuleUri = new Uri("file:///c:/dap-ws#Prog"),
        Range = new RDCore.SDK.Model.Source.SourceRange(new(7, 4), new(7, 14)),
    };

    // what the user is told to do depends on the edits: code that does not compile is corrected to go on; any other edit needs the program run again. The
    // console line and the refusal say the same.
    [TestMethod]
    [DataRow(true, false, nameof(DebuggerMessages.EditsAdviceCorrect), DisplayName = "compile errors only")]
    [DataRow(false, true, nameof(DebuggerMessages.EditsAdviceRunAgain), DisplayName = "restarts only")]
    [DataRow(true, true, nameof(DebuggerMessages.EditsAdviceCorrectOrRunAgain), DisplayName = "both")]
    public async Task TheEditsTheProgramDoesNotHave_AreSaidWithWhatToDoAboutThem(bool uncompiled, bool restart, string adviceKey)
    {
        await using var session = new Session();
        await StartedAndWaitingAsync(session);
        var edits = new[] { (uncompiled, CallerDoesNotCompile), (restart, CalleeLocalRemoved) }.Where(edit => edit.Item1).Select(edit => edit.Item2).ToArray();
        Listing(session, edits);
        var advice = DebuggerMessages.ResourceManager.GetString(adviceKey)!;
        var others = new[] { DebuggerMessages.EditsAdviceCorrect, DebuggerMessages.EditsAdviceRunAgain, DebuggerMessages.EditsAdviceCorrectOrRunAgain }.Where(text => text != advice);

        await session.EditStatus.CheckAsync(CancellationToken.None, afterLoad: true);
        var said = await session.NextAsync<OutputEvent>(output => output.Category == OutputEventCategory.Console);
        var refusal = await Assert.ThrowsAsync<JsonRpcException>(() => session.SendAsync(new ContinueArguments { ThreadId = 1 }));

        foreach (var text in new[] { said.Output, refusal.Message })
        {
            StringAssert.Contains(text, advice);
            Assert.IsFalse(others.Any(other => text.Contains(other, StringComparison.Ordinal)), $"one advice only: {text}");
            foreach (var edit in edits)
            {
                StringAssert.Contains(text, $"{edit.Name}: {edit.Reason}");
            }
        }
    }

    // the program goes on with the edits loaded while it waited: where it was, and what it held, may not be what the client shows.
    [TestMethod]
    public async Task GoingOnAfterAnEditWasLoaded_SaysTheFramesAndTheVariablesAreNotWhatTheyWere()
    {
        await using var session = new Session();
        await StartedAndWaitingAsync(session);
        Listing(session);
        session.Debugging.ResumeAsync(null, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Suspended()));

        await session.EditStatus.CheckAsync(CancellationToken.None, afterLoad: true);
        _ = await session.SendAsync(new ContinueArguments { ThreadId = 1 });

        var invalidated = await session.NextAsync<InvalidatedEvent>();
        CollectionAssert.AreEquivalent(new[] { InvalidatedAreas.Stacks, InvalidatedAreas.Variables }, invalidated.Areas!.ToArray());
    }

    [TestMethod]
    [DataRow(false, true, DisplayName = "no edit was loaded")]
    [DataRow(true, false, DisplayName = "a client that does not say it takes it")]
    public async Task GoingOn_IsNotSaidToInvalidateAnything_WhenNothingIs_OrTheClientDoesNotTakeIt(bool loaded, bool supportsInvalidated)
    {
        await using var session = new Session();
        session.Workspace.StartAsync(default!, default!, default).ReturnsForAnyArgs(Task.FromResult(Suspended()));
        session.Debugging.StackAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(StackAt(line: 12)));
        await session.StartAsync(supportsInvalidated: supportsInvalidated);
        await session.LaunchAsync();
        _ = await session.SendAsync(new ConfigurationDoneArguments());
        _ = await session.NextAsync<StoppedEvent>();
        Listing(session);
        session.Debugging.ResumeAsync(null, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Suspended()));
        if (loaded)
        {
            await session.EditStatus.CheckAsync(CancellationToken.None, afterLoad: true);
        }

        _ = await session.SendAsync(new ContinueArguments { ThreadId = 1 });
        _ = await session.NextAsync<StoppedEvent>();
        await FlushedAsync(session);

        Assert.AreEqual(0, session.Pending<InvalidatedEvent>().Count);
    }

    // the program is over: it has no edits it does not have, and what was said of them is not so any more.
    [TestMethod]
    public async Task AProgramThatIsEnded_HasNoEditItDoesNotHave()
    {
        await using var session = new Session();
        await StartedAndWaitingAsync(session);
        Listing(session, CalleeLocalRemoved);
        await session.EditStatus.CheckAsync(CancellationToken.None, afterLoad: true);
        Assert.HasCount(1, session.EditStatus.Current.Edits);
        session.Debugging.TerminateAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(new HostDebugAck()));
        session.Debugging.EditStatusAsync(Arg.Any<CancellationToken>()).Returns(new HostDebugEditStatusResult());

        _ = await session.SendAsync(new TerminateArguments());

        Assert.IsEmpty(session.EditStatus.Current.Edits);
    }

    // no editor tells the adapter of an edit: what is saved is loaded when the program is asked to go on, and checked, before the program is asked.
    [TestMethod]
    public async Task GoingOn_LoadsWhatIsSavedFirst_ThenChecksTheEdits_ThenRelays()
    {
        await using var session = new Session();
        await StartedAndWaitingAsync(session);
        var order = new List<string>();
        session.Workspace.When(workspace => workspace.ReloadChangedAsync(Arg.Any<CancellationToken>())).Do(_ => order.Add("reload"));
        session.Debugging.When(debugging => debugging.EditStatusAsync(Arg.Any<CancellationToken>())).Do(_ => order.Add("check"));
        session.Debugging.When(debugging => debugging.ResumeAsync(null, Arg.Any<CancellationToken>())).Do(_ => order.Add("resume"));
        session.Debugging.ResumeAsync(null, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Suspended()));

        _ = await session.SendAsync(new ContinueArguments { ThreadId = 1 });
        _ = await session.NextAsync<StoppedEvent>();

        // the check after the first stop may come in before them: what matters is what comes right before the program is asked.
        var resumed = order.IndexOf("resume");
        Assert.IsGreaterThanOrEqualTo(2, resumed, string.Join(", ", order));
        CollectionAssert.AreEqual(new[] { "reload", "check" }, order.GetRange(resumed - 2, 2).ToArray(), string.Join(", ", order));
    }

    // an edit saved while the program waits that it can't take, loaded as the program is asked to go on: refused, with the edit, and said.
    [TestMethod]
    public async Task GoingOn_WithASavedEditTheProgramCannotTake_IsRefused_WithTheEdit()
    {
        await using var session = new Session();
        await StartedAndWaitingAsync(session);
        session.Workspace.ReloadChangedAsync(Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            // as the reload does once the host has the saved file: the program that waits is checked against it.
            Listing(session, CalleeLocalRemoved);
            await session.EditStatus.CheckAsync(CancellationToken.None, afterLoad: true);
            return (string?)null;
        });

        var refusal = await Assert.ThrowsAsync<JsonRpcException>(() => session.SendAsync(new ContinueArguments { ThreadId = 1 }));

        StringAssert.Contains(refusal.Message, "Prog.Callee: a local was removed");
        _ = await session.NextAsync<OutputEvent>(output => output.Category == OutputEventCategory.Console && output.Output.Contains("Prog.Callee", StringComparison.Ordinal));
        await session.Debugging.DidNotReceiveWithAnyArgs().ResumeAsync(default, default);
    }

    [TestMethod]
    public async Task AGoto_LoadsWhatIsSavedFirst()
    {
        await using var session = new Session();
        await StartedAndWaitingAsync(session);
        var order = new List<string>();
        session.Workspace.When(workspace => workspace.ReloadChangedAsync(Arg.Any<CancellationToken>())).Do(_ => order.Add("reload"));
        session.Debugging.When(debugging => debugging.GotoAsync(Arg.Any<int>(), null, Arg.Any<CancellationToken>())).Do(_ => order.Add("goto"));
        session.Debugging.GotoAsync(Arg.Any<int>(), null, Arg.Any<CancellationToken>()).Returns(Task.FromResult(new HostDebugGotoResult { Moved = true }));

        _ = await session.SendAsync(new GotoArguments { ThreadId = 1, TargetId = 3 });

        CollectionAssert.AreEqual(new[] { "reload", "goto" }, order.ToArray());
    }

    // what is saved can't be gone on with - a file that can't be read, a module the host could not load, one renamed - or could not be loaded at all: the client
    // is refused, with why, and told it, and the program waits on the code it had.
    [TestMethod]
    [DataRow(false, DisplayName = "what the reload says")]
    [DataRow(true, DisplayName = "a reload that fails")]
    public async Task GoingOn_WithSavedCodeThatCannotBeLoaded_IsRefused_WithWhy(bool throws)
    {
        await using var session = new Session();
        await StartedAndWaitingAsync(session);
        if (throws)
        {
            session.Workspace.ReloadChangedAsync(Arg.Any<CancellationToken>()).Returns<Task<string?>>(_ => throw new IOException("the disk is gone"));
        }
        else
        {
            session.Workspace.ReloadChangedAsync(Arg.Any<CancellationToken>()).Returns("Prog.bas can't be read");
        }

        var refusal = await Assert.ThrowsAsync<JsonRpcException>(() => session.SendAsync(new ContinueArguments { ThreadId = 1 }));

        var why = throws ? "the disk is gone" : "Prog.bas can't be read";
        StringAssert.Contains(refusal.Message, why);
        StringAssert.Contains((await session.NextAsync<OutputEvent>(output => output.Category == OutputEventCategory.StandardError)).Output, why);
        await session.Debugging.DidNotReceiveWithAnyArgs().ResumeAsync(default, default);
    }

    // two requests to go on, the second while the first is loading what is saved: the second loads, checks and goes on once the first has started the program.
    // The handlers are asked directly, so that the second is in the adapter before the first is let go: what a request does before it waits is done by the time
    // its handler returns the task.
    [TestMethod]
    public async Task TwoRequestsToGoOn_LoadCheckAndGoOn_OneAfterTheOther()
    {
        await using var session = new Session();
        await StartedAndWaitingAsync(session);
        var order = new List<string>();
        var release = new TaskCompletionSource();
        session.Workspace.ReloadChangedAsync(Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            bool first;
            lock (order)
            {
                order.Add("reload");
                first = order.Count == 1;
            }

            if (first)
            {
                await release.Task;
            }

            return (string?)null;
        });
        session.Debugging.ResumeAsync(Arg.Any<StepKind?>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(Suspended()));

        var first = session.Adapter.Handle(new ContinueArguments { ThreadId = 1 }, CancellationToken.None);
        var second = session.Adapter.Handle(new NextArguments { ThreadId = 1 }, CancellationToken.None);
        string[] whileHeld;
        lock (order)
        {
            whileHeld = [.. order];
        }

        var secondWaited = !second.IsCompleted;
        release.SetResult();
        await Task.WhenAll(first, second).WaitAsync(Patience);

        CollectionAssert.AreEqual(new[] { "reload" }, whileHeld, "the second does not load while the first is loading");
        Assert.IsTrue(secondWaited, "the second waits for the first");
        lock (order)
        {
            Assert.HasCount(2, order, "the second loads once the first is done with its own");
        }
    }
}
