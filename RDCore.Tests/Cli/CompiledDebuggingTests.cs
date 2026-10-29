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

    // Main calls Spin for as long as the program runs, and Spin counts for seconds in a loop that compiles.
    private static readonly string Spinning = Program(
        "Public Sub Main()",
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
            // the first run is cancelled once it runs, which is a break: Spin's activation then is the interpreter's, and the next one is compiled again.
            Assert.AreEqual(ExecutionOutcome.Suspended, first.Outcome, first.ErrorMessage);
            var running = Resume(provider);
            await WaitForAsync(() => provider.Execution.State is ProgramState.Running && SpinIsCompiled(provider));
            var walkedBack = WalkBacksOfSpin(provider);

            _ = await SetBreakpoints(provider, 9);
            var hit = await running;

            Assert.AreEqual((ExecutionOutcome.Suspended, 9), (hit.Outcome, hit.ErrorLine), hit.ErrorMessage);

            // under the random walk-back the activation may have been handed to the interpreter before the breakpoint was set, which stops it all the same.
            if (!RDCore.Runtime.Compiled.CompiledExecution.WalksBackAtRandom)
            {
                Assert.IsGreaterThan(walkedBack, WalkBacksOfSpin(provider), "the compiled activation that was running walked back to stop there");
            }
            Assert.IsTrue((await Terminate(provider)).Acted);
        }, cancelWhen: provider => provider.Execution.State is ProgramState.Running, debug: true, engine: VBExecutionEngine.Compiled);
    }
}
