using NSubstitute;
using RDCore.CLI.Host;
using RDCore.Runtime.Execution;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Tests.Cli;

/// <summary>
/// A program that was run to be resumed waits at a <c>Stop</c> (<strong>MS-VBAL 5.4.2.11</strong>), wherever it is - in the middle of a statement of the procedure that
/// called the one that stopped, even - and goes on from the instruction after it, or one instruction, or one line of the caller.
/// </summary>
[TestClass]
public sealed class ResumeTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

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

            var pipeline = RuntimeExecutionPipeline.Create(Session, provider.Image, Substitute.For<IVerboseMessageBuilder>(), cancellation);
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

    [TestMethod]
    public async Task AStep_AtTheLastInstruction_EndsTheProgram()
        => await WithRunAsync(Program("Public Total As Long", "Public Sub Main()", "    Stop", "    Total = 1", "End Sub"), async run =>
        {
            Assert.IsTrue((await run.StartAsync()).IsSuspended);
            Assert.IsTrue((await Step(run, StepKind.Over)).IsSuspended, "before Total = 1");

            AssertCompleted(await Step(run, StepKind.Over));
            Assert.AreEqual(1, run.Total());
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
