using System.IO.Pipelines;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Client;
using RDCore.Runtime.Support.Host;
using RDCore.CLI.Host.Handlers;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Services;
using OmniSharpLanguageServer = OmniSharp.Extensions.LanguageServer.Server.LanguageServer;
using RDCore.SDK.Platform.Components;

namespace RDCore.Tests.Cli;

/// <summary>
/// A program that runs under a debugger is paused and ended over the host's connection while the request that runs it runs. Each request waited for the one
/// before it, and the request that runs a program is done when the program is: a pause or an end sent to a program that never ends waited forever.
/// </summary>
[TestClass]
public sealed class ProgramControlOverTheConnectionTests
{
    private static readonly (string Name, string Source)[] NoClasses = [];

    // what keeps a test from hanging: no answer it waits for takes nearly as long, on any machine.
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(5);

    // run again, it loops from the start: the module's variables are the session's, and stay from one run to the next.
    private static readonly string StopsTheFirstTimeThenLoops = string.Join("\r\n",
        "Public Total As Long",
        "Public Sub Main()",
        "    Total = Total + 1",
        "    If Total = 1 Then Stop",
        "    Do While True",
        "        Total = Total + 1",
        "    Loop",
        "End Sub") + "\r\n";

    private static readonly string StopsThenLoops = string.Join("\r\n",
        "Public Total As Long",
        "Public Sub Main()",
        "    Stop",
        "    Do While True",
        "        Total = Total + 1",
        "    Loop",
        "End Sub") + "\r\n";

    [TestMethod]
    public async Task APause_SentWhileAResumedProgramRuns_StopsIt_AndTheResumeAnswersSuspended()
        => await ConnectedAsync(async (provider, client, resumed) =>
        {
            var running = client.SendRequest<ExecuteResult>(new HostDebugResumeParams(), CancellationToken.None);
            await resumed.WaitAsync(Patience);
            Assert.AreEqual(ProgramState.Running, provider.Execution.State);

            var ack = await client.SendRequest<DebugAck>(new HostDebugPauseParams(), CancellationToken.None).WaitAsync(Patience);
            var paused = await running.WaitAsync(Patience);

            Assert.IsTrue(ack.Acted);
            Assert.AreEqual(ExecutionOutcome.Suspended, paused.Outcome, paused.ErrorMessage);
            Assert.IsTrue((await client.SendRequest<DebugAck>(new HostDebugTerminateParams(), CancellationToken.None).WaitAsync(Patience)).Acted);
        });

    [TestMethod]
    public async Task ATerminate_SentWhileAResumedProgramRuns_EndsIt()
        => await ConnectedAsync(async (provider, client, resumed) =>
        {
            var running = client.SendRequest<ExecuteResult>(new HostDebugResumeParams(), CancellationToken.None);
            await resumed.WaitAsync(Patience);
            Assert.AreEqual(ProgramState.Running, provider.Execution.State);

            var ack = await client.SendRequest<DebugAck>(new HostDebugTerminateParams(), CancellationToken.None).WaitAsync(Patience);

            Assert.IsTrue(ack.Acted);
            Assert.AreEqual(ExecutionOutcome.Halted, (await running.WaitAsync(Patience)).Outcome);
            Assert.AreEqual(ProgramState.Idle, provider.Execution.State);
        });

    // ---- a stop asked of a program that is started and not executing yet ----

    // a program is running from the moment it is started, and executes once it has its turn and is set up: the turn is taken, and told, in between. What is
    // asked of it there is asked of it as it starts: a run starts as one that was never stopped, and a stop asked earlier was lost.
    private static async Task<ExecuteResult> RunAskingAsItIsTakingItsTurn(EnvironmentSessionProvider provider, Func<Task<ExecuteResult>> run, Action ask)
    {
        void Asked(bool running)
        {
            if (running)
            {
                provider.Session.Turn.Running -= Asked;
                Assert.AreEqual(ProgramState.Running, provider.Execution.State);
                ask();
            }
        }

        provider.Session.Turn.Running += Asked;
        return await run().WaitAsync(Patience);
    }

    [TestMethod]
    public async Task APause_AskedOfAProgramThatIsStartedAndNotExecutingYet_StopsIt()
        => await ModuleWorkspace.InspectAsync(NoClasses, StopsTheFirstTimeThenLoops, async (provider, first, run) =>
        {
            Assert.AreEqual(ExecutionOutcome.Suspended, first.Outcome);
            var paused = false;

            var result = await RunAskingAsItIsTakingItsTurn(provider, run, () => paused = provider.Execution.Pause());

            Assert.IsTrue(paused);
            Assert.AreEqual(ExecutionOutcome.Suspended, result.Outcome, result.ErrorMessage);
            Assert.IsTrue((await provider.Execution.TerminateAsync(wipe: false)).Acted);
        }, debug: true);

    [TestMethod]
    public async Task ATerminate_AskedOfAProgramThatIsStartedAndNotExecutingYet_EndsIt()
        => await ModuleWorkspace.InspectAsync(NoClasses, StopsTheFirstTimeThenLoops, async (provider, first, run) =>
        {
            Assert.AreEqual(ExecutionOutcome.Suspended, first.Outcome);
            Task<DebugAck>? terminated = null;

            var result = await RunAskingAsItIsTakingItsTurn(provider, run, () => terminated = provider.Execution.TerminateAsync(wipe: false));

            Assert.AreEqual(ExecutionOutcome.Halted, result.Outcome, result.ErrorMessage);
            var ended = await terminated!.WaitAsync(Patience);
            Assert.IsTrue(ended.Acted);
            Assert.IsFalse(ended.Stopping, "it was ended");
            Assert.AreEqual(ProgramState.Idle, provider.Execution.State);
        }, debug: true);

    // a program in a call outside it stops when the call returns: the end asked of it stands, and whoever asked is not kept waiting for it - and is told the
    // program is still stopping.
    [TestMethod]
    public async Task ATerminate_WhoseWaitIsStopped_IsAnsweredAtOnce_ThatTheProgramIsStillStopping_AndTheEndStands()
        => await ModuleWorkspace.InspectAsync(NoClasses, StopsTheFirstTimeThenLoops, async (provider, first, run) =>
        {
            Assert.AreEqual(ExecutionOutcome.Suspended, first.Outcome);
            DebugAck? answeredBeforeTheEnd = null;

            var result = await RunAskingAsItIsTakingItsTurn(provider, run, () =>
            {
                var terminated = provider.Execution.TerminateAsync(wipe: false, new CancellationToken(canceled: true));
                answeredBeforeTheEnd = terminated.IsCompletedSuccessfully ? terminated.Result : null;
            });

            Assert.IsNotNull(answeredBeforeTheEnd, "answered at once");
            Assert.IsTrue(answeredBeforeTheEnd.Acted);
            Assert.IsTrue(answeredBeforeTheEnd.Stopping);
            Assert.AreEqual(ExecutionOutcome.Halted, result.Outcome, result.ErrorMessage);
        }, debug: true);

    // the looping program, which waits at the Stop it begins with, and the host's program requests served over a connection as the host serves them. The test
    // is told when the resume has begun: the program is running from then on.
    private static Task ConnectedAsync(Func<EnvironmentSessionProvider, LanguageClient, Task, Task> test)
        => ModuleWorkspace.InspectAsync(NoClasses, StopsThenLoops, async (provider, first, _) =>
        {
            Assert.AreEqual(ExecutionOutcome.Suspended, first.Outcome);

            using var timeout = new CancellationTokenSource(Patience);
            var toServer = new Pipe();
            var toClient = new Pipe();
            var resume = new BegunResume(new HostDebugResumeHandler(provider, NullLogger<HostDebugResumeHandler>.Instance));
            var serving = OmniSharpLanguageServer.From(options => options
                .WithInput(toServer.Reader)
                .WithOutput(toClient.Writer)
                .WithEveryRequestAnswered()
                .AddHandler(resume)
                .AddHandler(new HostDebugPauseHandler(provider))
                .AddHandler(new HostDebugTerminateHandler(provider))
                .WithLifecycleReceiver(), timeout.Token);
            var connecting = LanguageClient.From(options => options.WithInput(toClient.Reader).WithOutput(toServer.Writer), timeout.Token);
            await Task.WhenAll(serving, connecting).WaitAsync(Patience);
            using var server = await serving;
            using var client = await connecting;

            await test(provider, client, resume.Begun);
        }, debug: true);

    // the host's resume, which says when it has begun: a resume sets the program running before it first waits, and is answered when the program next stops.
    private sealed class BegunResume(HostDebugResumeHandler resume) : IJsonRpcRequestHandler<HostDebugResumeParams, ExecuteResult>
    {
        private readonly TaskCompletionSource _begun = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Begun => _begun.Task;

        public Task<ExecuteResult> Handle(HostDebugResumeParams request, CancellationToken cancellationToken)
        {
            var resumed = resume.Handle(request, cancellationToken);
            _begun.TrySetResult();
            return resumed;
        }
    }
}
