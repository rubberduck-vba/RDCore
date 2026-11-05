using RDCore.SDK.Platform;
using System.Globalization;

namespace RDCore.Tests.Platform;

/// <summary>
/// Work that recurses as deep as the code it reads runs on a thread with a stack of <see cref="LargeStackThread.StackSize"/>, as if it ran on the caller's: what
/// it returns and throws, in the caller's culture.
/// </summary>
[TestClass]
public sealed class LargeStackThreadTests
{
    // a recursion that a thread's default stack of 1 MB does not hold: about 200 bytes a level, 50,000 levels.
    private static int Recurse(int depth) => depth == 0 ? 0 : 1 + Recurse(depth - 1);

    [TestMethod]
    public void Run_ReturnsWhatTheWorkReturns_OnAStackLargerThanAThreadsDefault()
        => Assert.AreEqual(50_000, LargeStackThread.Run(() => Recurse(50_000)));

    [TestMethod]
    public void Run_ThrowsWhatTheWorkThrows()
    {
        var thrown = Assert.ThrowsExactly<InvalidOperationException>(() => LargeStackThread.Run<int>(() => throw new InvalidOperationException("work")));

        Assert.AreEqual("work", thrown.Message);
    }

    [TestMethod]
    public void Run_InsideRun_RunsOnTheSameThread()
    {
        var (outer, inner) = LargeStackThread.Run(() => (Environment.CurrentManagedThreadId, LargeStackThread.Run(() => Environment.CurrentManagedThreadId)));

        Assert.AreEqual(outer, inner);
        Assert.AreNotEqual(Environment.CurrentManagedThreadId, outer);
    }

    [TestMethod]
    public void Run_RunsInTheCallersCulture()
    {
        var (culture, uiCulture) = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = (CultureInfo.GetCultureInfo("fr-CA"), CultureInfo.GetCultureInfo("fr-CA"));

            Assert.AreEqual(("fr-CA", "fr-CA"), LargeStackThread.Run(() => (CultureInfo.CurrentCulture.Name, CultureInfo.CurrentUICulture.Name)));
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = (culture, uiCulture);
        }
    }

    [TestMethod]
    public async Task Start_CompletesWhenTheWorkDoes()
    {
        var depth = 0;

        await LargeStackThread.Start(() => depth = Recurse(50_000));

        Assert.AreEqual(50_000, depth);
    }

    [TestMethod]
    public async Task Start_IsFaultedWithWhatTheWorkThrows()
        => await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => LargeStackThread.Start(() => throw new InvalidOperationException("work")));

    [TestMethod]
    public async Task Start_IsCancelledWhenTheWorkIsCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var started = LargeStackThread.Start(() => cancellation.Token.ThrowIfCancellationRequested());

        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => started);
        Assert.IsTrue(started.IsCanceled);
    }
}
