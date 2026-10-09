using System.Collections.Concurrent;
using System.IO.Pipelines;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OmniSharp.Extensions.DebugAdapter.Client;
using OmniSharp.Extensions.DebugAdapter.Protocol.Events;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;
using OmniSharp.Extensions.DebugAdapter.Server;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.JsonRpc.Server;
using RDCore.LanguageServer.Debugging;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;

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
            Debugging.SetBreakpointsAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<int>>(), Arg.Any<CancellationToken>())
                .Returns(call => Task.FromResult(new HostDebugBreakpointsResult
                {
                    Breakpoints = [.. call.Arg<IReadOnlyList<int>>().Select(line => new HostBreakpoint(line, true))],
                }));
            Adapter = new ProgramDebugAdapter(Workspace, Debugging, Relay, NullLogger<ProgramDebugAdapter>.Instance);
        }

        public async Task StartAsync(bool linesStartAt1 = true)
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
                options.OnStopped(stopped => _events.Enqueue(stopped));
                options.OnOutput(output => _events.Enqueue(output));
                options.OnExited(exited => _events.Enqueue(exited));
                options.OnTerminated(terminated => _events.Enqueue(terminated));
            });
            Server = await starting.WaitAsync(Patience);
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

        await session.Debugging.Received(1).SetBreakpointsAsync("Prog", Arg.Is<IReadOnlyList<int>>(lines => lines.SequenceEqual(new[] { 11, 19 })), Arg.Any<CancellationToken>());
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

        await session.Debugging.Received(1).SetBreakpointsAsync("Prog", Arg.Is<IReadOnlyList<int>>(lines => lines.SequenceEqual(new[] { 11 })), Arg.Any<CancellationToken>());
        Assert.AreEqual(11, response.Breakpoints.Single().Line);
    }

    [TestMethod]
    public async Task ABreakpoint_OnALineThatNoStatementBeginsOn_IsNotVerified()
    {
        await using var session = new Session();
        session.Debugging.SetBreakpointsAsync("Prog", Arg.Any<IReadOnlyList<int>>(), Arg.Any<CancellationToken>())
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
        session.Workspace.OpenAsync(Arg.Any<CancellationToken>()).Returns(opening.Task);
        await session.StartAsync();

        var launching = session.LaunchAsync();
        var setting = session.SendAsync(new SetBreakpointsArguments
        {
            Source = new Source { Path = Path },
            Breakpoints = new Container<SourceBreakpoint>(new SourceBreakpoint { Line = 2 }),
        });
        await Task.Delay(200);
        Assert.IsFalse(setting.IsCompleted, "there is no workspace to set a breakpoint in yet");

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

        _ = await Assert.ThrowsAsync<JsonRpcException>(() => session.SendAsync(new ConfigurationDoneArguments()).WaitAsync(TimeSpan.FromMilliseconds(500)));
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
        await Task.Delay(200);

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
        await Task.Delay(200);
        Assert.AreEqual(0, session.Pending<TerminatedEvent>().Count);
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

        _ = await session.SendAsync(new EvaluateArguments { Expression = "1", Context = EvaluateArgumentsContext.Repl });

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

        _ = await session.SendAsync(new EvaluateArguments { Expression = "Noisy()", FrameId = 0, Context = EvaluateArgumentsContext.Repl });

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
}
