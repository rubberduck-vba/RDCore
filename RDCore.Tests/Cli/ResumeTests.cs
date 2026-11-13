using NSubstitute;
using RDCore.Runtime.Support.Host;
using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Frames;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.Runtime.Semantics;

namespace RDCore.Tests.Cli;

/// <summary>
/// A program that was run to be resumed waits at a <c>Stop</c> (<strong>MS-VBAL 5.4.2.11</strong>), wherever it is - in the middle of a statement of the procedure that
/// called the one that stopped, even - and goes on from the instruction after it, or one instruction, or one line of the caller.
/// </summary>
[TestClass]
public sealed class ResumeTests
{
    // a cap on a hang, not a budget: each wait is on the event itself, which a loaded machine brings as late as it does.
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(5);

    private static string Program(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    /// <summary>A program that is run by the pipeline of a session that has loaded it, under a gate.</summary>
    private sealed class Run(EnvironmentSessionProvider provider) : IDisposable
    {
        private readonly RuntimeOutputBuffer _output = new();
        private SuspendableExecution? _execution;

        public IRuntimeSession Session => provider.Session;

        public IReadOnlyList<string> Output => [.. _output.Lines.Select(line => line.Trim())];

        public SuspendableExecution Execution => _execution!;

        public async Task<ExecutionStop> StartAsync(CancellationToken cancellation = default)
        {
            // the host has run the program once to load it; this is the first run of a session that has just been loaded.
            SessionWipe.End(Session);
            Session.Halt.Clear();
            provider.Output.Target = _output;

            var pipeline = RuntimeExecutionPipeline.Create(Session, provider.Image, Substitute.For<IErrorDetailsRecorder>(), cancellation);
            Assert.IsTrue(Session.Symbols.TryResolveValue("Program", GlobalSymbols.UnresolvedSymbol, out var module));
            Assert.IsTrue(Session.Symbols.TryResolveValue("Main", module!, out var main));
            var entry = (VBTypeMemberSymbol)main!;

            _execution = new SuspendableExecution(Session);
            return await _execution.StartAsync(() => pipeline.Invoker.Invoke(entry, Session.Symbols.Resolver, [])).WaitAsync(Patience);
        }

        public long Total()
        {
            Assert.IsTrue(Session.Symbols.TryResolveValue("Program", GlobalSymbols.UnresolvedSymbol, out var module));
            Assert.IsTrue(Session.Symbols.TryResolveValue("Total", module!, out var total));
            return Convert.ToInt64(Session.Symbols.Resolver.GetValue(total!).Value.BoxedValue);
        }

        public void Dispose()
        {
            _execution?.Dispose();
            provider.Output.Target = NullRuntimeOutput.Instance;
        }
    }

    private static async Task WithRunAsync(string program, Func<Run, Task> test)
        => await ModuleWorkspace.InspectAsync([], program, async (provider, _, _) =>
        {
            using var run = new Run(provider);
            await test(run);
        });

    private static Task<ExecutionStop> Resume(Run run) => run.Execution.ResumeAsync().WaitAsync(Patience);

    private static Task<ExecutionStop> Step(Run run, StepKind kind) => run.Execution.StepAsync(kind).WaitAsync(Patience);

    private static void AssertCompleted(ExecutionStop stop)
    {
        Assert.IsFalse(stop.IsSuspended, "the program waits");
        Assert.IsTrue(stop.Result!.Value.IsSuccess);
    }

    [TestMethod]
    public async Task AProgramThatIsResumed_GoesOnFromTheStatementAfterTheStop()
        => await WithRunAsync(Program(
            "Public Total As Long",
            "Public Sub Main()",
            "    Debug.Print \"before\"",
            "    Total = 7",
            "    Stop",
            "    Total = Total + 1",
            "    Debug.Print \"after\"",
            "End Sub"), async run =>
        {
            var stop = await run.StartAsync();

            Assert.IsTrue(stop.IsSuspended);
            Assert.AreEqual(RuntimeHaltKind.Break, stop.Kind);
            Assert.AreEqual(4, stop.Location!.Value.Range.Start.Line, "the Stop");
            CollectionAssert.AreEqual(new[] { "before" }, run.Output.ToArray());
            Assert.AreEqual(7, run.Total());
            Assert.IsTrue(run.Execution.IsSuspended);
            Assert.AreEqual(1, run.Session.CallStack.Depth);

            AssertCompleted(await Resume(run));

            CollectionAssert.AreEqual(new[] { "before", "after" }, run.Output.ToArray());
            Assert.AreEqual(8, run.Total());
            Assert.IsFalse(run.Execution.IsSuspended);
            Assert.AreEqual(0, run.Session.CallStack.Depth);
        });

    [TestMethod]
    public async Task AProgramThatStoppedInAFunctionCalledInTheMiddleOfAStatement_FinishesTheStatement()
        => await WithRunAsync(Program(
            "Public Total As Long",
            "Public Sub Main()",
            "    Total = 100 + Compute() * 2",
            "    Debug.Print Total",
            "End Sub",
            "Private Function Compute() As Long",
            "    Compute = 20",
            "    Stop",
            "    Compute = Compute + 1",
            "End Function"), async run =>
        {
            var stop = await run.StartAsync();

            Assert.IsTrue(stop.IsSuspended);
            Assert.AreEqual(2, run.Session.CallStack.Depth, "Main is in the middle of the statement that called Compute");
            Assert.AreEqual(0, run.Total(), "the assignment is waiting for the function");

            AssertCompleted(await Resume(run));

            Assert.AreEqual(142, run.Total());
            CollectionAssert.AreEqual(new[] { "142" }, run.Output.ToArray());
        });

    [TestMethod]
    public async Task AProgramThatStopsAgain_IsSuspendedAgain()
        => await WithRunAsync(Program(
            "Public Total As Long",
            "Public Sub Main()",
            "    Total = 1",
            "    Stop",
            "    Total = 2",
            "    Stop",
            "    Total = 3",
            "End Sub"), async run =>
        {
            Assert.IsTrue((await run.StartAsync()).IsSuspended);
            Assert.AreEqual(1, run.Total());

            var second = await Resume(run);
            Assert.IsTrue(second.IsSuspended);
            Assert.AreEqual(5, second.Location!.Value.Range.Start.Line);
            Assert.AreEqual(2, run.Total());

            AssertCompleted(await Resume(run));
            Assert.AreEqual(3, run.Total());
        });

    [TestMethod]
    public async Task AFailedAssert_SuspendsTheProgram_AndItGoesOnAfterIt()
        => await WithRunAsync(Program(
            "Public Total As Long",
            "Public Sub Main()",
            "    Total = 1",
            "    Debug.Assert Total = 2",
            "    Total = 3",
            "End Sub"), async run =>
        {
            var stop = await run.StartAsync();

            Assert.IsTrue(stop.IsSuspended);
            Assert.AreEqual(3, stop.Location!.Value.Range.Start.Line, "the assert");
            Assert.AreEqual(1, run.Total());

            AssertCompleted(await Resume(run));
            Assert.AreEqual(3, run.Total());
        });

    [TestMethod]
    public async Task AProgramThatIsOver_CannotBeResumed()
        => await WithRunAsync(Program("Public Sub Main()", "    Stop", "End Sub"), async run =>
        {
            Assert.IsTrue((await run.StartAsync()).IsSuspended);
            AssertCompleted(await Resume(run));

            Assert.ThrowsExactly<InvalidOperationException>(() => run.Execution.ResumeAsync());
        });

    [TestMethod]
    public async Task AProgramThatEnds_AfterItIsResumed_IsEnded()
        => await WithRunAsync(Program(
            "Public Total As Long",
            "Public Sub Main()",
            "    Stop",
            "    Total = 1",
            "    End",
            "End Sub"), async run =>
        {
            Assert.IsTrue((await run.StartAsync()).IsSuspended);

            var stop = await Resume(run);

            Assert.IsFalse(stop.IsSuspended);
            Assert.AreEqual(RuntimeHaltKind.End, run.Session.Halt.Pending);
        });

    [TestMethod]
    public async Task ABreakAskedForFromOutside_SuspendsTheProgramBeforeTheInstruction_AndIsAnsweredOnce()
        => await WithRunAsync(Program(
            "Public Total As Long",
            "Public Sub Main()",
            "    Total = 1",
            "    Total = Total + 1",
            "    Debug.Print Total",
            "End Sub"), async run =>
        {
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();

            var stop = await run.StartAsync(cancellation.Token);

            Assert.IsTrue(stop.IsSuspended);
            Assert.AreEqual(2, stop.Location!.Value.Range.Start.Line, "before the first statement");
            Assert.AreEqual(0, run.Total());

            AssertCompleted(await Resume(run));
            CollectionAssert.AreEqual(new[] { "2" }, run.Output.ToArray());
        });

    // ---- abandoning ----

    [TestMethod]
    public async Task AProgramThatIsAbandoned_DoesNotGoOn_AndLeavesTheSessionAsItMadeIt()
        => await WithRunAsync(Program(
            "Public Total As Long",
            "Public Sub Main()",
            "    Total = 4",
            "    Stop",
            "    Total = 5",
            "    Debug.Print \"not reached\"",
            "End Sub"), async run =>
        {
            Assert.IsTrue((await run.StartAsync()).IsSuspended);

            run.Execution.Abandon();

            Assert.IsFalse(run.Execution.IsSuspended);
            Assert.AreEqual(4, run.Total());
            Assert.IsEmpty(run.Output);
            Assert.AreEqual(0, run.Session.CallStack.Depth);
            Assert.IsNull(run.Session.Halt.Pending);
            Assert.IsNull(run.Session.Halt.Gate);
        });

    [TestMethod]
    public async Task AProgramThatIsAbandoned_InAFunctionCalledInAnExpression_UnwindsWithoutWaitingAgain()
        => await WithRunAsync(Program(
            "Public Total As Long",
            "Public Sub Main()",
            "    Total = Compute() + Compute()",
            "End Sub",
            "Private Function Compute() As Long",
            "    Stop",
            "    Compute = 1",
            "End Function"), async run =>
        {
            Assert.IsTrue((await run.StartAsync()).IsSuspended);

            run.Execution.Abandon();

            Assert.AreEqual(0, run.Total());
            Assert.AreEqual(0, run.Session.CallStack.Depth);
        });

    // ---- stepping ----

    [TestMethod]
    public async Task AStepInto_StopsAtTheNextInstruction()
        => await WithRunAsync(Program(
            "Public Total As Long",
            "Public Sub Main()",
            "    Stop",
            "    Total = 1",
            "    Total = Total + 10",
            "End Sub"), async run =>
        {
            Assert.IsTrue((await run.StartAsync()).IsSuspended);

            var first = await Step(run, StepKind.Into);
            Assert.IsTrue(first.IsSuspended);
            Assert.AreEqual(3, first.Location!.Value.Range.Start.Line, "before Total = 1");
            Assert.AreEqual(0, run.Total());

            var second = await Step(run, StepKind.Into);
            Assert.AreEqual(4, second.Location!.Value.Range.Start.Line);
            Assert.AreEqual(1, run.Total(), "the statement that was stepped over is done, not half done");

            AssertCompleted(await Resume(run));
            Assert.AreEqual(11, run.Total());
        });

    // a step from the last statement of a block's branch stops at the block's closing line, where every branch ends, as MS-VBA stops there; so is the frame
    // that waits there. The jump that ends a branch is on no line, and is gone through. The steps after it go on past the block. The block is written at
    // line 3, after Total = 7; each line it stops at, in turn, is in "steps". A single-line If's Then part goes on to the next line, past its Else part.
    [TestMethod]
    [DataRow("If Total = 7 Then|    Stop|Else|    Total = 0|End If", "7,8", DisplayName = "the Then branch of an If block with an Else")]
    [DataRow("If Total = 0 Then|    Total = 0|ElseIf Total = 7 Then|    Stop|End If", "7,8", DisplayName = "an ElseIf branch")]
    [DataRow("If Total = 0 Then|    Total = 0|Else|    Stop|End If", "7,8", DisplayName = "an Else branch")]
    [DataRow("If Total = 7 Then|    If Total > 0 Then|        Stop|    End If|End If", "6,7,8", DisplayName = "an If block that ends an If block's branch")]
    [DataRow("Select Case Total|    Case 7|        Stop|    Case Else|        Total = 0|End Select", "8,9", DisplayName = "a Case")]
    [DataRow("Select Case Total|    Case 0|        Total = 0|    Case Else|        Stop|End Select", "8,9", DisplayName = "a Case Else")]
    [DataRow("With New Collection|    Stop|End With", "5,6", DisplayName = "a With block")]
    [DataRow("If Total = 7 Then Stop Else Total = 0", "4,5", DisplayName = "a single-line If with an Else")]
    public async Task AStepToTheEndOfABlocksBranch_StopsAtTheBlocksClosingLine(string block, string steps)
        => await WithRunAsync(Program([
            "Public Total As Long",
            "Public Sub Main()",
            "    Total = 7",
            .. block.Split('|').Select(line => "    " + line),
            "    Total = Total + 1",
            "End Sub"]), async run =>
        {
            Assert.IsTrue((await run.StartAsync()).IsSuspended);

            var seen = new List<string>();
            foreach (var _ in steps.Split(','))
            {
                var stop = await Step(run, StepKind.Into);
                Assert.IsTrue(stop.IsSuspended, $"after the steps to {string.Join(",", seen)}");
                var frame = (CallStackFrame)run.Session.CallStack.Frames.First();
                seen.Add($"{stop.Location?.Range.Start.Line.ToString() ?? "?"}{(frame.CurrentLocation?.Range.Start.Line == stop.Location?.Range.Start.Line ? "" : " (frame elsewhere)")}");
            }

            Assert.AreEqual(steps, string.Join(",", seen));
            AssertCompleted(await Resume(run));
            Assert.AreEqual(8, run.Total());
        });

    [TestMethod]
    public async Task AStepInto_AtACall_StopsInTheProcedureThatIsCalled()
        => await WithRunAsync(Program(
            "Public Total As Long",
            "Public Sub Main()",
            "    Stop",
            "    Helper",
            "    Total = Total + 1",
            "End Sub",
            "Private Sub Helper()",
            "    Total = 10",
            "End Sub"), async run =>
        {
            Assert.IsTrue((await run.StartAsync()).IsSuspended);
            Assert.IsTrue((await Step(run, StepKind.Into)).IsSuspended);
            Assert.AreEqual(1, run.Session.CallStack.Depth);

            var inside = await Step(run, StepKind.Into);

            Assert.IsTrue(inside.IsSuspended);
            Assert.AreEqual(7, inside.Location!.Value.Range.Start.Line, "the first line of Helper");
            Assert.AreEqual(2, run.Session.CallStack.Depth);
        });

    [TestMethod]
    public async Task AStepOver_AtACall_MakesTheCall_AndStopsAfterIt()
        => await WithRunAsync(Program(
            "Public Total As Long",
            "Public Sub Main()",
            "    Stop",
            "    Helper",
            "    Total = Total + 1",
            "End Sub",
            "Private Sub Helper()",
            "    Total = 10",
            "End Sub"), async run =>
        {
            Assert.IsTrue((await run.StartAsync()).IsSuspended);
            Assert.IsTrue((await Step(run, StepKind.Over)).IsSuspended, "before Helper");

            var after = await Step(run, StepKind.Over);

            Assert.IsTrue(after.IsSuspended);
            Assert.AreEqual(4, after.Location!.Value.Range.Start.Line, "before Total = Total + 1");
            Assert.AreEqual(10, run.Total(), "Helper has run");
            Assert.AreEqual(1, run.Session.CallStack.Depth);
        });

    [TestMethod]
    public async Task AStepOut_StopsInTheProcedureThatCalled()
        => await WithRunAsync(Program(
            "Public Total As Long",
            "Public Sub Main()",
            "    Helper",
            "    Total = Total + 1",
            "End Sub",
            "Private Sub Helper()",
            "    Stop",
            "    Total = 10",
            "    Total = Total + 5",
            "End Sub"), async run =>
        {
            Assert.IsTrue((await run.StartAsync()).IsSuspended);
            Assert.AreEqual(2, run.Session.CallStack.Depth);

            var out_ = await Step(run, StepKind.Out);

            Assert.IsTrue(out_.IsSuspended);
            Assert.AreEqual(3, out_.Location!.Value.Range.Start.Line, "before Total = Total + 1");
            Assert.AreEqual(15, run.Total(), "the rest of Helper has run");
            Assert.AreEqual(1, run.Session.CallStack.Depth);
        });

    // MS-VBA stops on the End Sub before the procedure returns, and leaves it at the next step.
    [TestMethod]
    public async Task AStep_AtTheLastStatement_StopsAtEndSub_AndTheNextEndsTheProgram()
        => await WithRunAsync(Program("Public Total As Long", "Public Sub Main()", "    Stop", "    Total = 1", "End Sub"), async run =>
        {
            Assert.IsTrue((await run.StartAsync()).IsSuspended);
            Assert.IsTrue((await Step(run, StepKind.Over)).IsSuspended, "before Total = 1");

            var end = await Step(run, StepKind.Over);
            Assert.IsTrue(end.IsSuspended, "the program waits at End Sub");
            Assert.AreEqual(4, end.Location!.Value.Range.Start.Line);
            Assert.AreEqual(1, run.Session.CallStack.Depth, "Main has not returned");
            Assert.AreEqual(1, run.Total());

            AssertCompleted(await Step(run, StepKind.Over));
        });

    [TestMethod]
    public async Task AStepOver_AtTheLastStatementOfACallee_StopsAtItsEndSub_ThenInTheCaller()
        => await WithRunAsync(Program(
            "Public Total As Long",
            "Public Sub Main()",
            "    Helper",
            "    Total = Total + 1",
            "End Sub",
            "Private Sub Helper()",
            "    Stop",
            "    Total = 10",
            "End Sub"), async run =>
        {
            Assert.IsTrue((await run.StartAsync()).IsSuspended);
            Assert.IsTrue((await Step(run, StepKind.Over)).IsSuspended, "before Total = 10");

            var end = await Step(run, StepKind.Over);
            Assert.AreEqual((8, 2), (end.Location!.Value.Range.Start.Line, run.Session.CallStack.Depth), "at Helper's End Sub");

            var back = await Step(run, StepKind.Over);
            Assert.AreEqual((3, 1), (back.Location!.Value.Range.Start.Line, run.Session.CallStack.Depth), "before Total = Total + 1");
        });

    // a step out does not stop at the End Sub of the procedure it leaves: it stops in the caller, which is at its own End Sub when the call was its last statement.
    [TestMethod]
    public async Task AStepOut_OfACallThatIsTheLastStatement_StopsAtTheCallersEndSub()
        => await WithRunAsync(Program(
            "Public Total As Long",
            "Public Sub Main()",
            "    Helper",
            "End Sub",
            "Private Sub Helper()",
            "    Stop",
            "    Total = 10",
            "End Sub"), async run =>
        {
            Assert.IsTrue((await run.StartAsync()).IsSuspended);

            var out_ = await Step(run, StepKind.Out);

            Assert.IsTrue(out_.IsSuspended);
            Assert.AreEqual((3, 1), (out_.Location!.Value.Range.Start.Line, run.Session.CallStack.Depth), "at Main's End Sub");
            Assert.AreEqual(10, run.Total());
            AssertCompleted(await Step(run, StepKind.Over));
        });

    [TestMethod]
    public async Task AStep_AtExitSub_StopsAtEndSub()
        => await WithRunAsync(Program(
            "Public Total As Long",
            "Public Sub Main()",
            "    Stop",
            "    Exit Sub",
            "    Total = 99",
            "End Sub"), async run =>
        {
            Assert.IsTrue((await run.StartAsync()).IsSuspended);
            Assert.AreEqual(3, (await Step(run, StepKind.Over)).Location!.Value.Range.Start.Line, "before Exit Sub");

            var end = await Step(run, StepKind.Over);

            Assert.AreEqual(5, end.Location!.Value.Range.Start.Line, "at End Sub, past the statement after the Exit Sub");
            Assert.AreEqual(0, run.Total());
            AssertCompleted(await Step(run, StepKind.Over));
        });

    [TestMethod]
    public async Task AProgramThatIsRunWithoutAGate_IsStillUnwoundByAStop()
        => await ModuleWorkspace.InspectAsync([], Program("Public Sub Main()", "    Stop", "End Sub"), (provider, result, _) =>
        {
            Assert.AreEqual(ExecutionOutcome.Interrupted, result.Outcome, result.ErrorMessage);
            Assert.IsNull(provider.Session.Halt.Gate);
            return Task.CompletedTask;
        });
}
