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

    // what the analysis pass does with an edit of the document: the module is parsed again and its code loaded again.
    private static void Reload(EnvironmentSessionProvider provider, string source)
    {
        var parse = new RDCore.Parsing.ModuleParser().Parse(new Uri(Path.Combine(ModuleWorkspace.Root, "Program.bas")), source);
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));
        Assert.IsTrue(provider.Session.Symbols.TryResolveValue("Program", RDCore.SDK.Model.Symbols.GlobalSymbols.UnresolvedSymbol, out var module));
        Assert.IsEmpty(new ModuleLoader(provider.Session, provider.Image, NSubstitute.Substitute.For<RDCore.SDK.Services.VerboseMessages.IVerboseMessageBuilder>()).Load(module!, parse));
    }

    [TestMethod]
    public async Task AResume_AfterABlankLineWasInsertedAboveTheCode_GoesOn()
        => await DebugAsync(Stopping, async (provider, _, _) =>
        {
            Reload(provider, "\r\n" + Stopping);

            Assert.AreEqual(ExecutionOutcome.Completed, (await Resume(provider)).Outcome);
        });

    [TestMethod]
    public async Task AResume_AfterAStatementWasEdited_IsRefused()
        => await DebugAsync(Stopping, async (provider, _, _) =>
        {
            Reload(provider, Stopping.Replace("Total + 10", "Total + 11"));

            var refused = await Resume(provider);

            Assert.AreEqual(ExecutionOutcome.Refused, refused.Outcome);
            Assert.AreEqual(ProgramState.Suspended, provider.Execution.State);
        });

    [TestMethod]
    public async Task AResume_AfterACommentWasEdited_GoesOn()
        => await DebugAsync(Stopping.Replace("    Stop", "    ' look here\r\n    Stop"), async (provider, _, _) =>
        {
            Reload(provider, Stopping.Replace("    Stop", "    ' look over there, instead\r\n    Stop"));

            Assert.AreEqual(ExecutionOutcome.Completed, (await Resume(provider)).Outcome);
        });

    [TestMethod]
    public void TheFingerprintsOfNoCode_AreNone()
        => Assert.AreEqual(0, new ProgramImage().Fingerprints().Count);

    // ---- moving the point the program goes on from ----

    private static Task<HostDebugGotoResult> Goto(EnvironmentSessionProvider provider, int line = 0, string? label = null)
        => new HostDebugGotoHandler(provider).Handle(new HostDebugGotoParams { Line = line, Label = label }, CancellationToken.None).WaitAsync(Patience);

    private static readonly string Skippable = Program(
        "Public Total As Long",
        "Public Sub Main()",
        "    Total = 1",
        "    Stop",
        "    Total = Total + 10",
        "    Total = Total + 100",
        "    Debug.Print Total",
        "End Sub");

    [TestMethod]
    public async Task AGoto_ForwardsOverAStatement_GoesOnFromThere()
        => await DebugAsync(Skippable, async (provider, _, _) =>
        {
            var moved = await Goto(provider, line: 5);

            Assert.IsTrue(moved.Moved, moved.Reason);
            Assert.AreEqual(5, moved.Line);

            var resumed = await Resume(provider);

            CollectionAssert.AreEqual(new[] { "101" }, Lines(resumed), "Total + 10 was not run");
        });

    [TestMethod]
    public async Task AGoto_BackwardsToAStatement_RunsItAgain()
        => await DebugAsync(Skippable, async (provider, _, _) =>
        {
            Assert.IsTrue((await Goto(provider, line: 2)).Moved);

            var again = await Resume(provider);

            Assert.AreEqual(ExecutionOutcome.Suspended, again.Outcome, "the Stop is run again");
            Assert.AreEqual(3, again.ErrorLine);
            Assert.AreEqual(ExecutionOutcome.Completed, (await Resume(provider)).Outcome);
        });

    [TestMethod]
    public async Task AGoto_ToTheStopItself_RunsTheStopAgain()
        => await DebugAsync(Skippable, async (provider, _, _) =>
        {
            Assert.IsTrue((await Goto(provider, line: 3)).Moved);

            var again = await Resume(provider);

            Assert.AreEqual(ExecutionOutcome.Suspended, again.Outcome);
            Assert.AreEqual(3, again.ErrorLine);
        });

    [TestMethod]
    public async Task AGoto_ToALineWithNoStatement_GoesToTheNextOne()
        => await DebugAsync(Program(
            "Public Total As Long",
            "Public Sub Main()",
            "    Stop",
            "",
            "    ' a comment",
            "    Total = 7",
            "    Debug.Print Total",
            "End Sub"), async (provider, _, _) =>
        {
            var moved = await Goto(provider, line: 3);

            Assert.IsTrue(moved.Moved, moved.Reason);
            Assert.AreEqual(5, moved.Line);
        });

    [TestMethod]
    public async Task AGoto_ToALabel_GoesOnFromTheLabel()
        => await DebugAsync(Program(
            "Public Total As Long",
            "Public Sub Main()",
            "    Stop",
            "    Total = 1",
            "Skip:",
            "    Total = Total + 5",
            "    Debug.Print Total",
            "End Sub"), async (provider, _, _) =>
        {
            Assert.IsTrue((await Goto(provider, label: "Skip")).Moved);

            CollectionAssert.AreEqual(new[] { "5" }, Lines(await Resume(provider)));
        });

    [TestMethod]
    public async Task AGoto_ToALineNumber_GoesOnFromTheLine()
        => await DebugAsync(Program(
            "Public Total As Long",
            "Public Sub Main()",
            "    Stop",
            "10  Total = 1",
            "20  Total = Total + 5",
            "30  Debug.Print Total",
            "End Sub"), async (provider, _, _) =>
        {
            Assert.IsTrue((await Goto(provider, label: "20")).Moved);

            CollectionAssert.AreEqual(new[] { "5" }, Lines(await Resume(provider)));
        });

    [TestMethod]
    public async Task AGoto_ToALabelThatIsNotThere_IsRefused_AndTheProgramGoesOnFromWhereItStopped()
        => await DebugAsync(Skippable, async (provider, _, _) =>
        {
            var moved = await Goto(provider, label: "Nowhere");

            Assert.IsFalse(moved.Moved);
            StringAssert.Contains(moved.Reason, "Nowhere");
            CollectionAssert.AreEqual(new[] { "111" }, Lines(await Resume(provider)));
        });

    [TestMethod]
    public async Task AGoto_PastTheLastStatement_IsRefused()
        => await DebugAsync(Skippable, async (provider, _, _) => Assert.IsFalse((await Goto(provider, line: 99)).Moved));

    [TestMethod]
    public async Task AGoto_WithNoProgramThatWaits_IsRefused()
        => await DebugAsync(Program("Public Sub Main()", "End Sub"), async (provider, _, _) =>
        {
            var moved = await Goto(provider, line: 0);

            Assert.IsFalse(moved.Moved);
            Assert.IsNotNull(moved.Reason);
        });

    [TestMethod]
    public async Task AGoto_AfterTheCodeChanged_IsRefused()
        => await DebugAsync(Skippable, async (provider, _, _) =>
        {
            Reload(provider, Skippable.Replace("Total + 100", "Total + 101"));

            Assert.IsFalse((await Goto(provider, line: 5)).Moved);
        });

    [TestMethod]
    public async Task AGoto_MovesTheActivationTheProgramWaitsIn_NotTheOnesThatCalledIt()
        => await DebugAsync(Program(
            "Public Total As Long",
            "Public Sub Main()",
            "    Helper",
            "    Debug.Print Total",
            "End Sub",
            "Private Sub Helper()",
            "    Stop",
            "    Total = 1",
            "    Total = Total + 20",
            "End Sub"), async (provider, _, _) =>
        {
            Assert.AreEqual(2, provider.Session.CallStack.Depth);
            Assert.IsTrue((await Goto(provider, line: 8)).Moved);

            CollectionAssert.AreEqual(new[] { "20" }, Lines(await Resume(provider)));
        });

    // ---- breakpoints ----

    private static Task<HostDebugBreakpointsResult> SetBreakpoints(EnvironmentSessionProvider provider, params int[] lines)
        => new HostDebugBreakpointsHandler(provider).Handle(new HostDebugBreakpointsParams { ModuleName = "Program", Lines = lines }, CancellationToken.None).WaitAsync(Patience);

    private static readonly string Counting = Program(
        "Public Total As Long",
        "Public Sub Main()",
        "    Dim i As Long",
        "    Stop",
        "    For i = 1 To 3",
        "        Total = Total + i",
        "        Debug.Print Total",
        "    Next",
        "    Debug.Print \"done\"",
        "End Sub");

    [TestMethod]
    public async Task ABreakpoint_SuspendsTheProgramBeforeItsLine_AndTheResumeGoesOnPastIt()
        => await DebugAsync(Counting, async (provider, _, _) =>
        {
            var set = await SetBreakpoints(provider, 8);

            Assert.IsTrue(set.Breakpoints.Single().Verified);

            var hit = await Resume(provider);

            Assert.AreEqual(ExecutionOutcome.Suspended, hit.Outcome, hit.ErrorMessage);
            Assert.AreEqual(8, hit.ErrorLine);
            CollectionAssert.AreEqual(new[] { "1", "3", "6" }, Lines(hit), "the loop ran, and the line has not");

            var done = await Resume(provider);

            Assert.AreEqual(ExecutionOutcome.Completed, done.Outcome);
            CollectionAssert.AreEqual(new[] { "done" }, Lines(done));
        });

    [TestMethod]
    public async Task ABreakpointInALoop_IsWaitedAtOnEveryRound()
        => await DebugAsync(Counting, async (provider, _, _) =>
        {
            _ = await SetBreakpoints(provider, 5);

            var rounds = new List<int>();
            var stop = await Resume(provider);
            while (stop.Outcome is ExecutionOutcome.Suspended)
            {
                rounds.Add(stop.ErrorLine);
                stop = await Resume(provider);
            }

            CollectionAssert.AreEqual(new[] { 5, 5, 5 }, rounds);
            Assert.AreEqual(ExecutionOutcome.Completed, stop.Outcome);
        });

    [TestMethod]
    public async Task ABreakpoint_ThatIsReplacedWithNone_IsNotWaitedAt()
        => await DebugAsync(Counting, async (provider, _, _) =>
        {
            _ = await SetBreakpoints(provider, 5);
            _ = await SetBreakpoints(provider);

            Assert.AreEqual(ExecutionOutcome.Completed, (await Resume(provider)).Outcome);
        });

    [TestMethod]
    public async Task ABreakpoint_OnALineWithNoStatement_IsNotVerified()
        => await DebugAsync(Counting, async (provider, _, _) =>
        {
            var set = await SetBreakpoints(provider, 0, 5, 99);

            CollectionAssert.AreEqual(new[] { false, true, false }, set.Breakpoints.Select(breakpoint => breakpoint.Verified).ToArray());
        });

    [TestMethod]
    public async Task ABreakpoint_AtTheLineAStepStopsAt_IsWaitedAtOnce()
        => await DebugAsync(Counting, async (provider, _, _) =>
        {
            _ = await SetBreakpoints(provider, 4);

            var stepped = await Resume(provider, StepKind.Over);

            Assert.AreEqual(ExecutionOutcome.Suspended, stepped.Outcome);
            Assert.AreEqual(4, stepped.ErrorLine);
            Assert.AreEqual(ExecutionOutcome.Suspended, (await Resume(provider, StepKind.Over)).Outcome, "the resume from there is a step to the next instruction, and not the same breakpoint");
        });

    [TestMethod]
    public async Task ABreakpoint_InAProcedureTheProgramCalls_IsWaitedAtThere()
        => await DebugAsync(Program(
            "Public Total As Long",
            "Public Sub Main()",
            "    Stop",
            "    Helper",
            "End Sub",
            "Private Sub Helper()",
            "    Total = 1",
            "    Total = 2",
            "End Sub"), async (provider, _, _) =>
        {
            _ = await SetBreakpoints(provider, 7);

            var hit = await Resume(provider);

            Assert.AreEqual(ExecutionOutcome.Suspended, hit.Outcome);
            Assert.AreEqual(7, hit.ErrorLine);
            Assert.AreEqual(2, provider.Session.CallStack.Depth);
        });

    [TestMethod]
    public async Task ABreakpoint_IsNotWaitedAtByAProgramNotUnderADebugger()
        => await ModuleWorkspace.InspectAsync(NoClasses, Program("Public Total As Long", "Public Sub Main()", "    Total = 1", "End Sub"), (provider, result, runAgain) =>
        {
            Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome);
            provider.Session.Halt.Breakpoints.Set(provider.Session.Symbols.TryResolveValue("Program", RDCore.SDK.Model.Symbols.GlobalSymbols.UnresolvedSymbol, out var module) ? module!.Uri.AbsoluteUri : string.Empty, [2]);
            return runAgain().ContinueWith(task => Assert.AreEqual(ExecutionOutcome.Completed, task.Result.Outcome));
        });
}
