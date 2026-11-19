using NSubstitute;
using RDCore.Runtime.Support.Host;
using RDCore.Runtime.Compiled;
using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Frames;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.Runtime.Semantics;
using static RDCore.Tests.Cli.CompiledDirectCallTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A break asked for while the program runs, answered where the interpreter answers it (before the instruction after the one during which it was asked
/// for, or before the first, when it was asked for before the program started), whichever procedures are compiled: each frame's program counter at
/// the instruction it waits before, or at the call it is making.
/// </summary>
[TestClass]
public sealed class CompiledBreakTests
{
    // a cap on a hang, not a budget: each wait is on the event itself, which a loaded machine brings as late as it does.
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(5);

    /// <summary>
    /// A program run again by the pipeline of a session that has run it to load it, under a gate: each line it prints is given to <c>said</c>, as it prints it.
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

    private static string LineOf(SourceLocation? location) => location?.Range.Start.Line.ToString() ?? "?";

    // where the program stopped, as the person who broke it sees it: the line it waits before, each frame at the line of the instruction its program
    // counter is at, innermost first, and Total.
    private static string Stopped(Run run, ExecutionStop stop)
    {
        if (!stop.IsSuspended)
        {
            return $"not suspended | Total {run.Total()}";
        }

        var frames = run.Session.CallStack.Frames.Cast<CallStackFrame>()
            .Select(frame => $"{frame.Procedure!.Name}@{LineOf(BreakpointTable.LocationOf(frame.Body!.Items[frame.Pc]))}");
        return $"{stop.Kind} @{LineOf(stop.Location)} | {string.Join(" < ", frames)} | Total {run.Total()}";
    }

    // the program loaded with engine, then started again: with a break already asked for, or asked for when it prints "go"; what it stopped at, then what
    // it did once resumed - its output and Total, and whether it broke again. Each procedure named ran compiled from its first statement to its end in the
    // run that loaded it, when the engine is the compiled one.
    private static async Task<(string Stopped, string Ended)> BreakAsync(string program, VBExecutionEngine engine, bool before, params string[] fullyCompiled)
    {
        (string Stopped, string Ended) seen = default;
        using var running = CompiledExecution.Running(engine);
        await ModuleWorkspace.InspectAsync([], program, async (provider, _, _) =>
        {
            if (engine is VBExecutionEngine.Compiled)
            {
                foreach (var procedure in fullyCompiled)
                {
                    var body = BodyOf(provider, procedure);
                    if (!CompiledExecution.HandsOverAtRandom)
                    {
                        Assert.AreEqual((0L, 0L), (body.Recoveries, body.WalkBacks), $"{procedure}'s recoveries and walk-backs");
                    }
                }
            }

            using var cancellation = new CancellationTokenSource();
            if (before)
            {
                await cancellation.CancelAsync();
            }

            using var run = new Run(provider, line =>
            {
                if (line == "go")
                {
                    cancellation.Cancel();
                }
            });

            var stop = await run.StartAsync(cancellation.Token);
            var stopped = Stopped(run, stop);
            var end = stop.IsSuspended ? await run.Execution.ResumeAsync().WaitAsync(Patience) : stop;
            seen = (stopped, $"{(end.IsSuspended ? "suspended again" : end.Result!.Value.IsSuccess ? "ended" : "failed")} | {string.Join(" ; ", run.Output)} | Total {run.Total()}");
        }, engine: engine);
        return seen;
    }

    // the program broken both ways: it stops where the interpreter stops it, and goes on from there as the interpreter does.
    private static async Task AssertBreaksAsync(string program, bool before, string stopped, string ended, params string[] fullyCompiled)
    {
        CompiledExecutionTests.RequirePayloads();
        var interpreted = await BreakAsync(program, VBExecutionEngine.Interpreter, before);
        Assert.AreEqual((stopped, ended), interpreted, "the interpreter");
        var compiled = await BreakAsync(program, VBExecutionEngine.Compiled, before, fullyCompiled);
        Assert.AreEqual(interpreted, compiled, "compiled code");
    }

    // a Main all compiled, started with a break already asked for: it breaks before its first statement, once.
    [TestMethod]
    public async Task AFullyCompiledMain_StartedWithABreakAskedFor_BreaksBeforeItsFirstStatement_Once()
        => await AssertBreaksAsync(Program(
            "Public Total As Long",
            "Public Sub Main()",
            "    Total = 1",
            "    Total = Total + 1",
            "    Total = Total * 5",
            "End Sub"), before: true,
            "Break @2 | Main@2 | Total 0",
            "ended |  | Total 10", "Main");

    // a break asked for while Main prints a Variant, which the interpreter prints, and compiled code goes on from without a loop: it breaks before the
    // statement after the Print.
    [TestMethod]
    public async Task ABreakAskedForInAStatementTheInterpreterRuns_IsAnsweredBeforeTheNext_InAStraightLine()
        => await AssertBreaksAsync(Program(
            "Public Total As Long",
            "Public Sub Main()",
            "    Debug.Print CVar(\"go\")",
            "    Total = 1",
            "    Total = Total + 1",
            "End Sub"), before: false,
            "Break @3 | Main@3 | Total 0",
            "ended | go | Total 2");

    // a break asked for while Main prints a String, which compiled code prints: it breaks before the statement after the Print, as the interpreter does.
    [TestMethod]
    public async Task ABreakAskedForInACompiledPrint_IsAnsweredBeforeTheNext_InAStraightLine()
        => await AssertBreaksAsync(Program(
            "Public Total As Long",
            "Public Sub Main()",
            "    Debug.Print \"g\" & \"o\"",
            "    Total = 1",
            "    Total = Total + 1",
            "End Sub"), before: false,
            "Break @3 | Main@3 | Total 0",
            "ended | go | Total 2", "Main");

    // a break asked for while Main prints, before it calls Fib, all compiled, which calls itself and has no loop: it breaks before the call, and Fib
    // runs only once the program is resumed.
    [TestMethod]
    public async Task ABreakAskedForBeforeACallOfARecursion_IsAnsweredBeforeTheCall()
        => await AssertBreaksAsync(Program(
            "Public Total As Long",
            "Public Sub Main()",
            "    Debug.Print \"go\"",
            "    Total = Fib(15)",
            "    Debug.Print Total",
            "End Sub",
            "Private Function Fib(ByVal N As Long) As Long",
            "    Dim A As Long, B As Long",
            "    If N < 2 Then",
            "        Fib = N",
            "    Else",
            "        A = Fib(N - 1)",
            "        B = Fib(N - 2)",
            "        Fib = A + B",
            "    End If",
            "End Function"), before: false,
            "Break @3 | Main@3 | Total 0",
            "ended | go ; 610 | Total 610", "Fib");

    // a break asked for while Spin's loop prints, at its fifth turn: it breaks in the loop, before the instruction after the Print, and not at a back edge
    // turns later.
    [TestMethod]
    public async Task ABreakAskedForInACompiledLoop_IsAnsweredBeforeTheNextInstruction()
        => await AssertBreaksAsync(Program(
            "Public Total As Long",
            "Public Sub Main()",
            "    Spin 1000",
            "    Debug.Print Total",
            "End Sub",
            "Private Sub Spin(ByVal N As Long)",
            "    Dim I As Long",
            "    For I = 1 To N",
            "        Total = Total + 1",
            "        If I = 5 Then Debug.Print \"go\"",
            "    Next",
            "End Sub"), before: false,
            "Break @10 | Spin@10 < Main@2 | Total 5",
            "ended | go ; 1000 | Total 1000");

    // a break asked for while Ping prints, last, at the fifth call Inner's loop makes of it; Inner, called by Outer's loop, and Outer all compiled: it breaks
    // at Ping's End Sub, before Ping returns, as MS-VBA stops there, and each frame below is at the call it is making.
    [TestMethod]
    public async Task ABreakAskedForDeepInCompiledCalls_StopsInTheInnermostFrame_EachFrameAtItsCall()
        => await AssertBreaksAsync(Program(
            "Public Total As Long, Pings As Long",
            "Public Sub Main()",
            "    Outer 3",
            "    Debug.Print Total",
            "End Sub",
            "Private Sub Outer(ByVal K As Long)",
            "    Dim J As Long",
            "    For J = 1 To K",
            "        Inner 1000",
            "    Next",
            "End Sub",
            "Private Sub Inner(ByVal N As Long)",
            "    Dim I As Long",
            "    For I = 1 To N",
            "        Total = Total + 1",
            "        Ping 1",
            "    Next",
            "End Sub",
            "Private Sub Ping(ByVal Times As Long)",
            "    Pings = Pings + Times",
            "    If Pings = 5 Then Debug.Print \"go\"",
            "End Sub"), before: false,
            "Break @21 | Ping@21 < Inner@15 < Outer@8 < Main@2 | Total 5",
            "ended | go ; 3000 | Total 3000", "Outer", "Inner");

    // a break asked for while Spin prints, last in an If block's branch, at its fifth turn: the jump that ends the branch is on no line and is gone through,
    // and the program breaks at the End If, as MS-VBA stops there.
    [TestMethod]
    public async Task ABreakAskedForAtTheEndOfAnIfBlocksBranch_IsAnsweredAtItsEndIf()
        => await AssertBreaksAsync(Program(
            "Public Total As Long",
            "Public Sub Main()",
            "    Spin 1000",
            "    Debug.Print Total",
            "End Sub",
            "Private Sub Spin(ByVal N As Long)",
            "    Dim I As Long",
            "    For I = 1 To N",
            "        Total = Total + 1",
            "        If I = 5 Then",
            "            Debug.Print \"go\"",
            "        End If",
            "    Next",
            "End Sub"), before: false,
            "Break @11 | Spin@11 < Main@2 | Total 5",
            "ended | go ; 1000 | Total 1000");
}
