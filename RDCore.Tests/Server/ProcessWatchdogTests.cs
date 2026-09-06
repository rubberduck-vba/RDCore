using RDCore.SDK.Server;

namespace RDCore.Tests.Server;

[TestClass]
public class ProcessWatchdogTests
{
    [TestMethod]
    public void Arm_DoesNotBlockTheCaller()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        ProcessWatchdog.Arm(0, graceSeconds: 30, terminate: _ => { });
        sw.Stop();

        Assert.IsTrue(sw.ElapsedMilliseconds < 1000, $"Arm blocked for {sw.ElapsedMilliseconds} ms");
    }

    [TestMethod]
    public async Task Arm_TerminatesWithTheGivenExitCode_AfterTheGracePeriod()
    {
        var terminated = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        ProcessWatchdog.Arm(42, graceSeconds: 0 /* clamped to 1 */, terminate: terminated.SetResult);

        var code = await terminated.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual(42, code);
    }
}
