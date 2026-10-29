using Microsoft.Extensions.Logging.Abstractions;
using RDCore.CLI.Host;
using RDCore.CLI.Host.Handlers;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Cli;

/// <summary>
/// A program under a debugger runs compiled where it can, until something only the interpreter does is asked for: a breakpoint, or a step. A compiled
/// activation that is running then is handed to the interpreter at its next safepoint, and stops where the interpreter stops.
/// </summary>
[TestClass]
public sealed class CompiledDebuggingTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private static string Program(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    // Main waits at its Stop, then calls Spin for as long as the program runs, and Spin counts for seconds in a loop that compiles.
    private static readonly string Spinning = Program(
        "Public Sub Main()",
        "    Stop",
        "    Do While True",
        "        Spin",
        "    Loop",
        "End Sub",
        "Private Sub Spin()",
        "    Dim i As Long",
        "    Dim n As Long",
        "    For i = 1 To 2000000000",
        "        n = n + 1",
        "    Next",
        "End Sub");

    private static Task<ExecuteSessionResult> Resume(EnvironmentSessionProvider provider)
        => new HostDebugResumeHandler(provider, NullLogger<HostDebugResumeHandler>.Instance)
            .Handle(new HostDebugResumeParams(), CancellationToken.None).WaitAsync(Patience);

    private static Task<HostDebugBreakpointsResult> SetBreakpoints(EnvironmentSessionProvider provider, params int[] lines)
        => new HostDebugBreakpointsHandler(provider).Handle(new HostDebugBreakpointsParams
        {
            ModuleName = "Program",
            Breakpoints = [.. lines.Select(line => new HostBreakpointLine { Line = line })],
        }, CancellationToken.None).WaitAsync(Patience);

    private static Task<HostDebugAck> Pause(EnvironmentSessionProvider provider)
        => new HostDebugPauseHandler(provider).Handle(new HostDebugPauseParams(), CancellationToken.None).WaitAsync(Patience);

    private static Task<HostDebugAck> Terminate(EnvironmentSessionProvider provider)
        => new HostDebugTerminateHandler(provider).Handle(new HostDebugTerminateParams(), CancellationToken.None).WaitAsync(Patience);

    // how often the compiled body of Spin was handed to the interpreter; none while it was never compiled.
    private static long WalkBacksOfSpin(EnvironmentSessionProvider provider)
        => provider.Image.Compiler.Results.Where(result => result.Procedure.Name == "Spin").Select(result => result.Result.Body?.WalkBacks ?? 0).Sum();

    private static bool SpinIsCompiled(EnvironmentSessionProvider provider)
        => provider.Image.Compiler.Results.Any(result => result.Procedure.Name == "Spin" && result.Result.Body is not null);

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (!condition())
        {
            Assert.IsLessThan(deadline, DateTime.UtcNow, "timed out");
            await Task.Delay(10);
        }
    }

    [TestMethod]
    public async Task ABreakpointSetWhileACompiledLoopRuns_IsWaitedAt_InTheLoop()
    {
        await ModuleWorkspace.InspectAsync([], Spinning, async (provider, first, runAgain) =>
        {
            // the program waits at Main's Stop, before Spin is called: the first call of Spin compiles it, and that activation is the one running once it is
            // compiled, counting for seconds.
            Assert.AreEqual((ExecutionOutcome.Suspended, 1), (first.Outcome, first.ErrorLine), first.ErrorMessage);
            try
            {
                var running = Resume(provider);
                await WaitForAsync(() => provider.Execution.State is ProgramState.Running && SpinIsCompiled(provider));
                var walkedBack = WalkBacksOfSpin(provider);

                _ = await SetBreakpoints(provider, 10);
                var hit = await running;

                Assert.AreEqual((ExecutionOutcome.Suspended, 10), (hit.Outcome, hit.ErrorLine), hit.ErrorMessage);

                // under the random walk-back the activation may have been handed to the interpreter before the breakpoint was set, which stops it all the same.
                if (!RDCore.Runtime.Compiled.CompiledExecution.WalksBackAtRandom)
                {
                    Assert.IsGreaterThan(walkedBack, WalkBacksOfSpin(provider), "the compiled activation that was running walked back to stop there");
                }
            }
            finally
            {
                // a program that is left running counts for seconds yet, on its own thread.
                _ = await Terminate(provider);
            }
        }, debug: true, engine: VBExecutionEngine.Compiled);
    }

    // Main waits at its Stop, then calls Outer, which runs compiled - no breakpoint is set - and calls Inner, which a Stop keeps interpreted, and which
    // stops the program at its third call.
    private static readonly string Calling = Program(
        "Public Sub Main()",
        "    Stop",
        "    Debug.Print Outer(5)",
        "End Sub",
        "Private Function Outer(ByVal n As Long) As Long",
        "    Dim i As Long, t As Long",
        "    For i = 1 To n",
        "        t = Inner(i)",
        "    Next",
        "    Outer = t",
        "End Function",
        "Private Function Inner(ByVal x As Long) As Long",
        "    If x = 3 Then Stop",
        "    Inner = x * 10",
        "End Function");

    // a compiled caller in the middle of its call is on the stack at the call, with its variables as they are.
    [TestMethod]
    public async Task ACompiledCaller_OfACalleeThatWaits_IsOnTheStackAtItsCall_WithItsVariables()
    {
        await ModuleWorkspace.InspectAsync([], Calling, async (provider, first, runAgain) =>
        {
            Assert.AreEqual((ExecutionOutcome.Suspended, 1), (first.Outcome, first.ErrorLine), first.ErrorMessage);
            try
            {
                var stopped = await Resume(provider);
                Assert.AreEqual((ExecutionOutcome.Suspended, 12), (stopped.Outcome, stopped.ErrorLine), stopped.ErrorMessage);

                var stack = provider.Execution.Stack().Frames;
                Assert.AreEqual(("Inner", "Outer", 7), (stack[0].Procedure, stack[1].Procedure, stack[1].Line));
                var outer = provider.Execution.Variables(stack[1].Id, HostVariableScope.Locals, 0).Variables.ToDictionary(variable => variable.Name, variable => variable.Value);
                Assert.AreEqual(("3", "20"), (outer["i"], outer["t"]), "the caller's variables, as they were when it made the call");
            }
            finally
            {
                _ = await Terminate(provider);
            }
        }, debug: true, engine: VBExecutionEngine.Compiled);
    }

    // the program goes on: the compiled caller reads back its variables, which the debugger could have changed while it waited, and goes on from its call.
    [TestMethod]
    public async Task ACompiledCaller_OfACalleeThatWaited_GoesOnFromItsCall()
    {
        await ModuleWorkspace.InspectAsync([], Calling, async (provider, first, runAgain) =>
        {
            Assert.AreEqual(ExecutionOutcome.Suspended, first.Outcome, first.ErrorMessage);
            var stopped = await Resume(provider);
            Assert.AreEqual((ExecutionOutcome.Suspended, 12), (stopped.Outcome, stopped.ErrorLine), stopped.ErrorMessage);

            var done = await Resume(provider);

            Assert.AreEqual(ExecutionOutcome.Completed, done.Outcome, done.ErrorMessage);
            CollectionAssert.AreEqual(new[] { "50" }, done.Output.Select(line => line.Trim()).ToArray());
        }, debug: true, engine: VBExecutionEngine.Compiled);
    }

    // the program run to the Stop in Inner, at stopLine, while Outer, compiled, is in the middle of its call; edited; and resumed.
    private static async Task<ExecuteSessionResult> EditedWhileACompiledCallerWaitsAsync(string program, string edited, int stopLine)
    {
        ExecuteSessionResult? resumed = null;
        await ModuleWorkspace.InspectAsync([], program, async (provider, first, runAgain) =>
        {
            Assert.AreEqual(ExecutionOutcome.Suspended, first.Outcome, first.ErrorMessage);
            try
            {
                var stopped = await Resume(provider);
                Assert.AreEqual((ExecutionOutcome.Suspended, stopLine), (stopped.Outcome, stopped.ErrorLine), stopped.ErrorMessage);
                Assert.AreEqual("Outer", provider.Execution.Stack().Frames[1].Procedure);

                // under the random walk-back Outer may have been handed to the interpreter before it made the call, and is edited as any frame is.
                if (!RDCore.Runtime.Compiled.CompiledExecution.WalksBackAtRandom)
                {
                    Assert.IsTrue(provider.Session.CallStack.Frames.OfType<RDCore.Runtime.Execution.Frames.CallStackFrame>().ElementAt(1).RunsCompiled,
                        "Outer is running compiled, in the middle of its call");
                }

                _ = await ModuleWorkspace.ReloadOnlyAsync(provider, ["Program"], ("Program", RDCore.SDK.Model.AST.Declarations.ModuleType.StdModule, edited));
                resumed = await Resume(provider);
            }
            finally
            {
                _ = await Terminate(provider);
            }
        }, debug: true, engine: VBExecutionEngine.Compiled);

        return resumed!;
    }

    // the compiled caller's procedure edited while the program waits in its callee: the caller goes on in the edited code once its call returns, from the
    // instruction after its call, with its variables and its loop as they are.
    [TestMethod]
    [DataRow("    Outer = t|    Outer = t + 1", "51", DisplayName = "a line below the call")]
    [DataRow("    Dim i As Long, t As Long|    Dim i As Long, t As Long\r\n    n = n", "50", DisplayName = "a line added above the loop the call is in")]
    [DataRow("    Next|    Next\r\n    t = t * 2", "100", DisplayName = "a line added after the loop the call is in")]
    public async Task AnEditOfACompiledCallerInTheMiddleOfItsCall_IsApplied_WhenTheCallReturns(string edit, string expected)
    {
        var (find, replace) = (edit.Split('|')[0], edit.Split('|')[1]);

        var resumed = await EditedWhileACompiledCallerWaitsAsync(Calling, Calling.Replace(find, replace, StringComparison.Ordinal), 12);

        Assert.AreEqual(ExecutionOutcome.Completed, resumed.Outcome, resumed.ErrorMessage);
        CollectionAssert.AreEqual(new[] { expected }, resumed.Output.Select(line => line.Trim()).ToArray());
    }

    // where the call it is in the middle of goes can't be told, or there is none: the edit is refused, never guessed.
    [TestMethod]
    [DataRow("        t = Inner(i)|        t = Inner(i + 0)", DisplayName = "the call edited")]
    [DataRow("        t = Inner(i)\r\n|", DisplayName = "the call removed")]
    [DataRow("    For i = 1 To n|    For i = 1 To n + 1", DisplayName = "the loop the call is in, its opening edited")]
    public async Task AnEditOfTheCallACompiledCallerIsInTheMiddleOf_IsRefused(string edit)
    {
        var (find, replace) = (edit.Split('|')[0], edit.Split('|')[1]);

        var resumed = await EditedWhileACompiledCallerWaitsAsync(Calling, Calling.Replace(find, replace, StringComparison.Ordinal), 12);

        Assert.AreEqual(ExecutionOutcome.Refused, resumed.Outcome, resumed.ErrorMessage);
        StringAssert.Contains(resumed.ErrorMessage, "Program.Outer");
    }

    // the call moved out of its loop into another: its loop's state is of a loop it is no longer in.
    [TestMethod]
    public async Task TheCallACompiledCallerIsInTheMiddleOf_MovedIntoAnotherLoop_IsRefused()
    {
        var program = Program(
            "Public Sub Main()",
            "    Stop",
            "    Debug.Print Outer(5)",
            "End Sub",
            "Private Function Outer(ByVal n As Long) As Long",
            "    Dim i As Long, j As Long, t As Long",
            "    For i = 1 To n",
            "        t = Inner(i)",
            "    Next",
            "    For j = 1 To 2",
            "        t = t + 1",
            "    Next",
            "    Outer = t",
            "End Function",
            "Private Function Inner(ByVal x As Long) As Long",
            "    If x = 3 Then Stop",
            "    Inner = x * 10",
            "End Function");
        var edited = program.Replace("        t = Inner(i)\r\n", "", StringComparison.Ordinal)
            .Replace("        t = t + 1\r\n", "        t = t + 1\r\n        t = Inner(i)\r\n", StringComparison.Ordinal);

        var resumed = await EditedWhileACompiledCallerWaitsAsync(program, edited, 15);

        Assert.AreEqual(ExecutionOutcome.Refused, resumed.Outcome, resumed.ErrorMessage);
        StringAssert.Contains(resumed.ErrorMessage, "Program.Outer");
    }

    // the callee raises an error after the edit: it is the call's, where the call now is in the edited code, and the caller's caller catches it.
    [TestMethod]
    public async Task AnErrorTheCalleeRaises_AfterAnEditOfTheCompiledCaller_IsTheCallsWhereItNowIs()
    {
        var program = Program(
            "Public Sub Main()",
            "    On Error GoTo Caught",
            "    Stop",
            "    Debug.Print Outer(5)",
            "    Exit Sub",
            "Caught:",
            "    Debug.Print \"caught \" & Err.Number",
            "End Sub",
            "Private Function Outer(ByVal n As Long) As Long",
            "    Dim i As Long, t As Long",
            "    For i = 1 To n",
            "        t = Inner(i)",
            "    Next",
            "    Outer = t",
            "End Function",
            "Private Function Inner(ByVal x As Long) As Long",
            "    If x = 3 Then Stop",
            "    Inner = x * 1000000000",
            "End Function");
        var edited = program.Replace("    Dim i As Long, t As Long\r\n", "    Dim i As Long, t As Long\r\n    n = n\r\n", StringComparison.Ordinal);

        var resumed = await EditedWhileACompiledCallerWaitsAsync(program, edited, 16);

        Assert.AreEqual(ExecutionOutcome.Completed, resumed.Outcome, resumed.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "caught 6" }, resumed.Output.Select(line => line.Trim()).ToArray());
    }

    // a loop paused where it goes round again is shown at its Next, as MS-VBA shows it: the back edge has no statement of its own.
    [TestMethod]
    public async Task ACompiledLoopPaused_IsShownWaitingAtItsNext()
    {
        await ModuleWorkspace.InspectAsync([], Spinning, async (provider, first, runAgain) =>
        {
            Assert.AreEqual((ExecutionOutcome.Suspended, 1), (first.Outcome, first.ErrorLine), first.ErrorMessage);
            try
            {
                var running = Resume(provider);
                await WaitForAsync(() => provider.Execution.State is ProgramState.Running && SpinIsCompiled(provider));

                Assert.IsTrue((await Pause(provider)).Acted);
                var paused = await running;

                // compiled code polls at the loop's back edge, and is handed to the interpreter there; under the random walk-back, at any line of the loop.
                Assert.AreEqual(ExecutionOutcome.Suspended, paused.Outcome, paused.ErrorMessage);
                if (RDCore.Runtime.Compiled.CompiledExecution.WalksBackAtRandom)
                {
                    CollectionAssert.Contains(new[] { 10, 11 }, paused.ErrorLine, "a line of the loop, and not none");
                }
                else
                {
                    Assert.AreEqual(11, paused.ErrorLine, "the loop's Next");
                }

                Assert.AreEqual("Spin", provider.Execution.Stack().Frames[0].Procedure);
            }
            finally
            {
                _ = await Terminate(provider);
            }
        }, debug: true, engine: VBExecutionEngine.Compiled);
    }
}
