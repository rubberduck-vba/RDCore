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

    // a compiled caller in the middle of its call is on the stack at the call, with its variables as they are; an edit of it is not applied yet.
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

                _ = await ModuleWorkspace.ReloadOnlyAsync(provider, ["Program"],
                    ("Program", RDCore.SDK.Model.AST.Declarations.ModuleType.StdModule, Calling.Replace("    Outer = t", "    Outer = t + 1", StringComparison.Ordinal)));
                var refused = await Resume(provider);
                Assert.AreEqual(ExecutionOutcome.Refused, refused.Outcome, refused.ErrorMessage);
                StringAssert.Contains(refused.ErrorMessage, "Program.Outer");
                StringAssert.Contains(refused.ErrorMessage, "runs compiled");
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
