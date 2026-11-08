using RDCore.SDK.Server;

namespace RDCore.Tests.Server;

/// <summary>
/// The watchdog waits out its grace period on a thread of its own, and only then terminates the process. The wait is the test's, held by a gate, so what
/// the watchdog does before it ends and after it is told apart by the gate and not by a clock.
/// </summary>
[TestClass]
public class ProcessWatchdogTests
{
    // not a timing: a watchdog that never waits or never terminates fails the test here instead of hanging the run.
    private static readonly TimeSpan HangGuard = TimeSpan.FromSeconds(30);

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task Arm_ReturnsWhileTheGracePeriodIsWaited_AndDoesNotTerminateBeforeItEnds()
    {
        var waiting = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
        var graceOver = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var terminated = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        // the grace period ends when the test says so, after Arm returns. A watchdog that waited on the caller's thread would hold Arm until the hang guard
        // ends the wait, and the process would be terminated by then.
        ProcessWatchdog.Arm(7, graceSeconds: 30, terminate: terminated.SetResult, wait: grace =>
        {
            waiting.SetResult(grace);
            _ = graceOver.Task.Wait(HangGuard);
        });

        Assert.AreEqual(TimeSpan.FromSeconds(30), await waiting.Task.WaitAsync(HangGuard));
        Assert.IsFalse(terminated.Task.IsCompleted, "the process is not terminated while the grace period is waited");

        graceOver.SetResult();
        Assert.AreEqual(7, await terminated.Task.WaitAsync(HangGuard));
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task Arm_WaitsAtLeastASecond_ThenTerminatesWithTheGivenExitCode()
    {
        var waited = TimeSpan.Zero;
        var terminated = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        ProcessWatchdog.Arm(42, graceSeconds: 0, terminate: terminated.SetResult, wait: grace => waited = grace);

        Assert.AreEqual(42, await terminated.Task.WaitAsync(HangGuard));
        Assert.AreEqual(TimeSpan.FromSeconds(1), waited, "a grace period of 0 is a second");
    }
}
