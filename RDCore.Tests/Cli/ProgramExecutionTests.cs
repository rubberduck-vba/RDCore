using Microsoft.Extensions.Logging.Abstractions;
using RDCore.CLI.Host;
using RDCore.CLI.Host.Handlers;
using RDCore.Runtime.Execution;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Cli;

/// <summary>
/// The host owns the program that runs in its session: under a debugger a <c>Stop</c> suspends it and the request that ran it answers so, a resume goes on from
/// there, and nothing that would wreck a program that waits is let through.
/// </summary>
[TestClass]
public sealed class ProgramExecutionTests
{
    private static readonly (string Name, string Source)[] NoClasses = [];

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private static string Program(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    private static Task<ExecuteSessionResult> Resume(EnvironmentSessionProvider provider, StepKind? step = null)
        => new HostDebugResumeHandler(provider, NullLogger<HostDebugResumeHandler>.Instance)
            .Handle(new HostDebugResumeParams { Step = step }, CancellationToken.None).WaitAsync(Patience);

    private static Task<HostDebugAck> Pause(EnvironmentSessionProvider provider)
        => new HostDebugPauseHandler(provider).Handle(new HostDebugPauseParams(), CancellationToken.None).WaitAsync(Patience);

    private static Task<HostDebugAck> Terminate(EnvironmentSessionProvider provider, bool wipe = false)
        => new HostDebugTerminateHandler(provider).Handle(new HostDebugTerminateParams { Wipe = wipe }, CancellationToken.None).WaitAsync(Patience);

    private static Task<DiscardSessionResult> Discard(EnvironmentSessionProvider provider, bool endProgram)
        => new HostDiscardHandler(provider, NullLogger<HostDiscardHandler>.Instance).Handle(new HostDiscardParams
        {
            ModuleUri = new Uri(provider.Image.Fingerprints().Keys.Single(), UriKind.Absolute),
            ModuleName = "Program",
            EndProgram = endProgram,
        }, CancellationToken.None).WaitAsync(Patience);

    private static string[] Lines(ExecuteSessionResult result) => [.. result.Output.Select(line => line.Trim())];

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (!condition())
        {
            Assert.IsLessThan(deadline, DateTime.UtcNow, "timed out");
            await Task.Delay(10);
        }
    }

    private static Task DebugAsync(string program, Func<EnvironmentSessionProvider, ExecuteSessionResult, Func<Task<ExecuteSessionResult>>, Task> test, TimeSpan? cancelAfter = null)
        => ModuleWorkspace.InspectAsync(NoClasses, program, test, cancelAfter, debug: true);

    private static readonly string Stopping = Program(
        "Public Total As Long",
        "Public Sub Main()",
        "    Debug.Print \"start\"",
        "    Total = Total + 1",
        "    Stop",
        "    Total = Total + 10",
        "    Debug.Print \"end\"",
        "End Sub");

    private static readonly string Looping = Program(
        "Public Total As Long",
        "Public Sub Main()",
        "    Do While True",
        "        Total = Total + 1",
        "    Loop",
        "End Sub");

    // ---- suspending and resuming ----

    [TestMethod]
    public async Task AStop_UnderADebugger_SuspendsTheProgram_AndTheRequestSaysWhere()
        => await DebugAsync(Stopping, (provider, result, _) =>
        {
            Assert.AreEqual(ExecutionOutcome.Suspended, result.Outcome, result.ErrorMessage);
            Assert.AreEqual(4, result.ErrorLine, "the Stop");
            CollectionAssert.AreEqual(new[] { "start" }, Lines(result));
            Assert.AreEqual(ProgramState.Suspended, provider.Execution.State);
            Assert.AreEqual(1, provider.Session.CallStack.Depth);
            return Task.CompletedTask;
        });

    [TestMethod]
    public async Task AResume_GoesOnFromTheStop_AndAnswersWhatTheProgramPrintedSince()
        => await DebugAsync(Stopping, async (provider, _, _) =>
        {
            var resumed = await Resume(provider);

            Assert.AreEqual(ExecutionOutcome.Completed, resumed.Outcome, resumed.ErrorMessage);
            CollectionAssert.AreEqual(new[] { "end" }, Lines(resumed));
            Assert.AreEqual(ProgramState.Idle, provider.Execution.State);
            Assert.AreEqual(0, provider.Session.CallStack.Depth);
        });

    [TestMethod]
    public async Task AStep_AnswersWhereTheProgramWaitsNext()
        => await DebugAsync(Stopping, async (provider, _, _) =>
        {
            var next = await Resume(provider, StepKind.Over);

            Assert.AreEqual(ExecutionOutcome.Suspended, next.Outcome, next.ErrorMessage);
            Assert.AreEqual(5, next.ErrorLine, "before Total = Total + 10");
            Assert.AreEqual(ProgramState.Suspended, provider.Execution.State);
        });

    [TestMethod]
    public async Task AResume_WithNoProgramThatWaits_IsRefused()
        => await DebugAsync(Program("Public Sub Main()", "End Sub"), async (provider, result, _) =>
        {
            Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome);

            var resumed = await Resume(provider);

            Assert.AreEqual(ExecutionOutcome.Refused, resumed.Outcome);
        });

    [TestMethod]
    public async Task AStop_NotUnderADebugger_StillEndsTheRun()
        => await ModuleWorkspace.InspectAsync(NoClasses, Stopping, (provider, result, _) =>
        {
            Assert.AreEqual(ExecutionOutcome.Interrupted, result.Outcome, result.ErrorMessage);
            Assert.AreEqual(ProgramState.Idle, provider.Execution.State);
            return Task.CompletedTask;
        });

    // ---- running again ----

    [TestMethod]
    public async Task ARun_WhileTheProgramWaits_StartsItOverFromTheTop()
        => await DebugAsync(Stopping, async (provider, _, runAgain) =>
        {
            var again = await runAgain().WaitAsync(Patience);

            Assert.AreEqual(ExecutionOutcome.Suspended, again.Outcome, again.ErrorMessage);
            CollectionAssert.AreEqual(new[] { "start" }, Lines(again), "from the top, and not from the Stop");
            Assert.AreEqual(1, provider.Session.CallStack.Depth, "the activations of the program that waited are gone");
            Assert.AreEqual(ProgramState.Suspended, provider.Execution.State);
        });

    [TestMethod]
    public async Task ARun_WhileAProgramRuns_IsRefused()
        => await DebugAsync(Looping, async (provider, first, runAgain) =>
        {
            Assert.AreEqual(ExecutionOutcome.Suspended, first.Outcome, "the cancellation of the request is a break");

            var running = Resume(provider);
            await WaitForAsync(() => provider.Execution.State is ProgramState.Running);

            var refused = await runAgain().WaitAsync(Patience);

            Assert.AreEqual(ExecutionOutcome.Refused, refused.Outcome);
            Assert.IsTrue((await Terminate(provider)).Acted);
            Assert.AreEqual(ExecutionOutcome.Halted, (await running).Outcome);
        }, cancelAfter: TimeSpan.FromMilliseconds(200));

    // ---- pausing ----

    [TestMethod]
    public async Task APause_StopsTheProgramThatRuns_AndTheRequestThatRanItAnswersSuspended()
        => await DebugAsync(Looping, async (provider, first, _) =>
        {
            Assert.AreEqual(ExecutionOutcome.Suspended, first.Outcome);
            var running = Resume(provider);
            await WaitForAsync(() => provider.Execution.State is ProgramState.Running);

            var ack = await Pause(provider);
            var paused = await running;

            Assert.IsTrue(ack.Acted);
            Assert.AreEqual(ExecutionOutcome.Suspended, paused.Outcome, paused.ErrorMessage);
            Assert.AreEqual(ProgramState.Suspended, provider.Execution.State);
            Assert.IsTrue((await Terminate(provider)).Acted);
        }, cancelAfter: TimeSpan.FromMilliseconds(200));

    [TestMethod]
    public async Task APause_WithNoProgram_ActsOnNothing()
        => await DebugAsync(Program("Public Sub Main()", "End Sub"), async (provider, _, _) => Assert.IsFalse((await Pause(provider)).Acted));

    // ---- terminating ----

    [TestMethod]
    public async Task ATerminate_EndsTheProgramThatWaits_AndKeepsWhatItMade()
        => await DebugAsync(Stopping, async (provider, _, _) =>
        {
            var ack = await Terminate(provider);

            Assert.IsTrue(ack.Acted);
            Assert.AreEqual(ProgramState.Idle, provider.Execution.State);
            Assert.AreEqual(0, provider.Session.CallStack.Depth);
            Assert.IsNull(provider.Session.Halt.Gate);
        });

    [TestMethod]
    public async Task ATerminate_ThatWipes_LeavesNothingOfTheProgram()
        => await DebugAsync(Stopping, async (provider, _, runAgain) =>
        {
            Assert.IsTrue((await Terminate(provider, wipe: true)).Acted);

            var again = await runAgain().WaitAsync(Patience);

            CollectionAssert.AreEqual(new[] { "start" }, Lines(again));
            Assert.IsTrue((await Terminate(provider)).Acted);
        });

    [TestMethod]
    public async Task ATerminate_WithNoProgram_ActsOnNothing()
        => await DebugAsync(Program("Public Sub Main()", "End Sub"), async (provider, _, _) => Assert.IsFalse((await Terminate(provider)).Acted));

    // ---- what would wreck a program that waits ----

    [TestMethod]
    public async Task ADiscard_OfAModule_WhileTheProgramWaits_KeepsTheModule()
        => await DebugAsync(Stopping, async (provider, _, _) =>
        {
            var discarded = await Discard(provider, endProgram: false);

            Assert.AreEqual(0, discarded.Discarded);
            Assert.AreEqual(ProgramState.Suspended, provider.Execution.State);
            Assert.AreEqual(ExecutionOutcome.Completed, (await Resume(provider)).Outcome, "the code it runs is still there");
        });

    [TestMethod]
    public async Task ADiscard_ThatEndsTheProgram_EndsItAndWipesTheSession()
        => await DebugAsync(Stopping, async (provider, _, _) =>
        {
            var discarded = await Discard(provider, endProgram: true);

            Assert.IsGreaterThan(0, discarded.Discarded);
            Assert.AreEqual(ProgramState.Idle, provider.Execution.State);
            Assert.AreEqual(0, provider.Session.CallStack.Depth);
        });

    // ---- source changed while the program waited ----

    [TestMethod]
    public async Task AResume_AfterTheCodeChanged_IsRefused_AndTheProgramStillWaits()
        => await DebugAsync(Stopping, async (provider, _, _) =>
        {
            var module = new Uri(provider.Image.Fingerprints().Keys.Single());
            var original = provider.Image.ToArray();

            // what the analysis pass does when the document is edited: the module is loaded again, as something else.
            provider.Image.Load(module, original.Take(0));
            var refused = await Resume(provider);

            Assert.AreEqual(ExecutionOutcome.Refused, refused.Outcome);
            StringAssert.Contains(refused.ErrorMessage, "changed");
            Assert.AreEqual(ProgramState.Suspended, provider.Execution.State);

            // and undone: the code is the code it was suspended with, and the program goes on.
            provider.Image.Load(module, original);
            var resumed = await Resume(provider);

            Assert.AreEqual(ExecutionOutcome.Completed, resumed.Outcome, resumed.ErrorMessage);
        });

    [TestMethod]
    public async Task AResume_AfterAnotherModuleWasLoaded_GoesOn()
        => await DebugAsync(Stopping, async (provider, _, _) =>
        {
            provider.Image.Load(new Uri("file:///c:/ws/Other.bas#Other"), []);

            Assert.AreEqual(ExecutionOutcome.Completed, (await Resume(provider)).Outcome);
        });

    [TestMethod]
    public void TheFingerprintsOfNoCode_AreNone()
        => Assert.AreEqual(0, new ProgramImage().Fingerprints().Count);
}
