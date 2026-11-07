using NSubstitute;
using RDCore.CLI.Host;
using RDCore.Runtime.Compiled;
using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Frames;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Services.VerboseMessages;
using static RDCore.Tests.Cli.CompiledDirectCallTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A break asked for from outside, answered by a program whose procedures are compiled: before the first statement of a procedure called while it is
/// asked for, and at the back edge of a loop compiled code runs, with the frame's program counter at the instruction the program waits before.
/// </summary>
[TestClass]
public sealed class CompiledBreakTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    /// <summary>
    /// A program run again by the pipeline of a session that has run it compiled to load it, under a gate: each line it prints is said to said, as it prints it.
    /// </summary>
    private sealed class Run : IDisposable
    {
        private readonly EnvironmentSessionProvider provider;
        private readonly List<string> _lines = [];
        private readonly RuntimeOutputBuffer output;
        private SuspendableExecution? _execution;

        public Run(EnvironmentSessionProvider provider, Action<string>? said = null)
        {
            this.provider = provider;
            output = new RuntimeOutputBuffer(line =>
            {
                _lines.Add(line.Trim());
                said?.Invoke(line.Trim());
            });
        }

        public IRuntimeSession Session => provider.Session;

        public IReadOnlyList<string> Output => [.. _lines, .. output.Lines.Select(line => line.Trim())];

        public SuspendableExecution Execution => _execution!;

        public async Task<ExecutionStop> StartAsync(CancellationToken cancellation)
        {
            SessionWipe.End(Session);
            Session.Halt.Clear();
            provider.Output.Target = output;

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

    // the program loaded by a session that runs compiled code, which runs it once to its end.
    private static async Task WithCompiledProgramAsync(string program, Func<EnvironmentSessionProvider, Task> test)
    {
        CompiledExecutionTests.RequirePayloads();
        using var running = CompiledExecution.Running(VBExecutionEngine.Compiled);
        await ModuleWorkspace.InspectAsync([], program, async (provider, _, _) => await test(provider), engine: VBExecutionEngine.Compiled);
    }

    // the procedure named ran in compiled code from its first statement to its end: none stepped, never handed to the interpreter.
    private static void AssertFullyCompiled(EnvironmentSessionProvider provider, string procedure)
    {
        var body = BodyOf(provider, procedure);
        if (!CompiledExecution.HandsOverAtRandom)
        {
            Assert.AreEqual((0L, 0L), (body.Recoveries, body.WalkBacks), $"{procedure}'s recoveries and walk-backs");
        }
    }

    // the current frame's program counter is at the instruction the program waits before.
    private static void AssertWaitsAtPc(Run run, ExecutionStop stop)
    {
        var frame = (CallStackFrame)run.Session.CallStack.Current!;
        Assert.AreEqual(stop.Location!.Value.Range.Start.Line, frame.Body!.Items[frame.Pc].Location!.Value.Range.Start.Line, "the frame's Pc");
    }

    private static Task<ExecutionStop> Resume(Run run) => run.Execution.ResumeAsync().WaitAsync(Patience);

    // a Main all compiled, started again with a break already asked for: it breaks before its first statement, and the break is answered once - resumed,
    // the program runs to its end.
    [TestMethod]
    public async Task AFullyCompiledMain_StartedWithABreakAskedFor_BreaksBeforeItsFirstStatement_Once()
        => await WithCompiledProgramAsync(Program(
            "Public Total As Long",
            "Public Sub Main()",
            "    Total = 1",
            "    Total = Total + 1",
            "    Total = Total * 5",
            "End Sub"), async provider =>
        {
            AssertFullyCompiled(provider, "Main");
            using var run = new Run(provider);
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();

            var stop = await run.StartAsync(cancellation.Token);

            Assert.IsTrue(stop.IsSuspended, "it breaks");
            Assert.AreEqual(RuntimeHaltKind.Break, stop.Kind);
            Assert.AreEqual(2, stop.Location!.Value.Range.Start.Line, "before the first statement");
            AssertWaitsAtPc(run, stop);
            Assert.AreEqual(0, run.Total());

            var end = await Resume(run);

            Assert.IsFalse(end.IsSuspended, "the break was answered");
            Assert.IsTrue(end.Result!.Value.IsSuccess);
            Assert.AreEqual(10, run.Total());
        });

    // a break asked for while Main prints, before it calls Spin, all compiled, whose loop asks whether to stop at its back edge: the program breaks in the
    // loop, before it is over, and goes on from there when it is resumed, without breaking again.
    [TestMethod]
    public async Task AFullyCompiledLoop_AnswersABreakAtItsBackEdge_Once()
        => await WithCompiledProgramAsync(Program(
            "Public Total As Long",
            "Public Sub Main()",
            "    Debug.Print \"go\"",
            "    Spin 100000",
            "    Debug.Print Total",
            "End Sub",
            "Private Sub Spin(ByVal N As Long)",
            "    Dim I As Long",
            "    For I = 1 To N",
            "        Total = Total + 1",
            "    Next",
            "End Sub"), async provider =>
        {
            AssertFullyCompiled(provider, "Spin");
            using var cancellation = new CancellationTokenSource();
            using var run = new Run(provider, line =>
            {
                if (line == "go")
                {
                    cancellation.Cancel();
                }
            });

            var stop = await run.StartAsync(cancellation.Token);

            Assert.IsTrue(stop.IsSuspended, "it breaks");
            Assert.IsLessThan(100000L, run.Total(), "before the loop is over");
            if (!CompiledExecution.HandsOverAtRandom)
            {
                Assert.AreEqual("Spin", ((CallStackFrame)run.Session.CallStack.Current!).Procedure!.Name);
                Assert.IsGreaterThan(0L, run.Total(), "the loop ran compiled until it asked");
                Assert.IsTrue(stop.Location!.Value.Range.Start.Line is >= 8 and <= 10, $"in the loop: line {stop.Location!.Value.Range.Start.Line}");
                AssertWaitsAtPc(run, stop);
            }

            var end = await Resume(run);

            Assert.IsFalse(end.IsSuspended, "the break was answered");
            Assert.IsTrue(end.Result!.Value.IsSuccess);
            CollectionAssert.AreEqual(new[] { "go", "100000" }, run.Output.ToArray(), string.Join(" ; ", run.Output));
        });

    // a break asked for while Main prints, before it calls Outer, whose loop calls Inner, both all compiled: the program breaks in Inner's loop, the innermost
    // frame, each frame below it at the call it is making, and goes on from there when it is resumed, without breaking again.
    [TestMethod]
    public async Task ABreakInACompiledCallOfACompiledProcedure_StopsInTheInnermostFrame_EachFrameAtItsCall()
        => await WithCompiledProgramAsync(Program(
            "Public Total As Long",
            "Public Sub Main()",
            "    Debug.Print \"go\"",
            "    Outer 3",
            "    Debug.Print Total",
            "End Sub",
            "Private Sub Outer(ByVal K As Long)",
            "    Dim J As Long",
            "    For J = 1 To K",
            "        Inner 100000",
            "    Next",
            "End Sub",
            "Private Sub Inner(ByVal N As Long)",
            "    Dim I As Long",
            "    For I = 1 To N",
            "        Total = Total + 1",
            "    Next",
            "End Sub"), async provider =>
        {
            AssertFullyCompiled(provider, "Outer");
            AssertFullyCompiled(provider, "Inner");
            using var cancellation = new CancellationTokenSource();
            using var run = new Run(provider, line =>
            {
                if (line == "go")
                {
                    cancellation.Cancel();
                }
            });

            var stop = await run.StartAsync(cancellation.Token);

            Assert.IsTrue(stop.IsSuspended, "it breaks");
            Assert.IsLessThan(300000L, run.Total(), "before the loops are over");
            if (!CompiledExecution.HandsOverAtRandom)
            {
                Assert.IsGreaterThan(0L, run.Total(), "Inner's loop ran compiled until it asked");
                Assert.IsLessThan(100000L, run.Total(), "in Inner's first call");
                Assert.IsTrue(stop.Location!.Value.Range.Start.Line is >= 14 and <= 16, $"in Inner's loop: line {stop.Location!.Value.Range.Start.Line}");
                AssertWaitsAtPc(run, stop);

                var frames = run.Session.CallStack.Frames.Cast<CallStackFrame>()
                    .Select(frame => $"{frame.Procedure!.Name}@{frame.Body!.Items[frame.Pc].Location!.Value.Range.Start.Line}").ToArray();
                CollectionAssert.AreEqual(new[] { $"Inner@{stop.Location!.Value.Range.Start.Line}", "Outer@9", "Main@3" }, frames, string.Join(" < ", frames));
            }

            var end = await Resume(run);

            Assert.IsFalse(end.IsSuspended, "the break was answered");
            Assert.IsTrue(end.Result!.Value.IsSuccess);
            CollectionAssert.AreEqual(new[] { "go", "300000" }, run.Output.ToArray(), string.Join(" ; ", run.Output));
        });
}
