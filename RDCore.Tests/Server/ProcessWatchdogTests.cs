using System.Diagnostics;
using RDCore.SDK.Server;

namespace RDCore.Tests.Server;

[TestClass]
public class ProcessWatchdogTests
{
    [TestMethod]
    public void Arm_DoesNotBlockTheCaller()
    {
        var stopwatch = Stopwatch.StartNew();
        ProcessWatchdog.Arm(0, graceSeconds: 30, terminate: _ => { });
        stopwatch.Stop();

        Assert.IsTrue(stopwatch.ElapsedMilliseconds < 1000, $"Arm blocked for {stopwatch.ElapsedMilliseconds} ms");
    }

    [TestMethod]
    public async Task Arm_TerminatesWithTheGivenExitCode_AfterTheGracePeriod()
    {
        var terminated = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        // graceSeconds 0 is clamped to a 1s minimum.
        ProcessWatchdog.Arm(42, graceSeconds: 0, terminate: terminated.SetResult);

        // the bound is for a watchdog that never fires, not for how soon it does: the wait is ended by the thread pool, which a runner busy with the rest of
        // the suite can keep from it for seconds after the watchdog has fired.
        var code = await terminated.Task.WaitAsync(TimeSpan.FromSeconds(60));

        Assert.AreEqual(42, code);
    }
}
