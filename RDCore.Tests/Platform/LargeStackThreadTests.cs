using RDCore.SDK.Platform;
using System.Globalization;
using System.Runtime.CompilerServices;

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

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Fail() => throw new InvalidOperationException("work");

    [TestMethod]
    public void Run_ThrowsWhatTheWorkThrows_WithTheStackTraceItWasThrownWith()
    {
        var thrown = Assert.ThrowsExactly<InvalidOperationException>(() => LargeStackThread.Run(Fail));

        Assert.AreEqual("work", thrown.Message);
        Assert.Contains(nameof(Fail), thrown.StackTrace ?? string.Empty);
    }

    [TestMethod]
    public void Run_OnTheThreadOfAProgram_RunsOnThatThread()
    {
        var (outer, inner) = (0, 0);
        var program = LargeStackThread.NewThread(() => (outer, inner) = (Environment.CurrentManagedThreadId, LargeStackThread.Run(() => Environment.CurrentManagedThreadId)), "test program");

        program.Start();
        program.Join();

        Assert.AreEqual(outer, inner);
    }

    [TestMethod]
    public void TheThreadOfAProgram_HasALargeStack()
    {
        var depth = 0;
        var program = LargeStackThread.NewThread(() => depth = Recurse(50_000), "test program");

        program.Start();
        program.Join();

        Assert.AreEqual(50_000, depth);
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

    [TestMethod]
    public async Task Start_RunsInTheCallersCulture()
    {
        var (culture, uiCulture) = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = (CultureInfo.GetCultureInfo("fr-CA"), CultureInfo.GetCultureInfo("fr-CA"));
            var seen = (string.Empty, string.Empty);

            await LargeStackThread.Start(() => seen = (CultureInfo.CurrentCulture.Name, CultureInfo.CurrentUICulture.Name));

            Assert.AreEqual(("fr-CA", "fr-CA"), seen);
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = (culture, uiCulture);
        }
    }

    // the threads are counted by a semaphore of the test's own, so that what the test holds is no other test's.
    [TestMethod]
    public async Task Start_WhenEveryThreadIsBusy_Waits_AndWorkCancelledWhileItWaits_NeverRuns()
    {
        using var threads = new SemaphoreSlim(1, 1);
        using var running = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var busy = LargeStackThread.Start(() =>
        {
            running.Set();
            release.Wait();
        }, threads, CancellationToken.None);
        running.Wait();

        using var cancellation = new CancellationTokenSource();
        var ran = false;
        var waiting = LargeStackThread.Start(() => ran = true, threads, cancellation.Token);
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => waiting);
        Assert.IsTrue(waiting.IsCanceled);
        Assert.IsFalse(ran, "the work never ran: no thread was free while it waited");

        release.Set();
        await busy;
        await LargeStackThread.Start(() => ran = true, threads, CancellationToken.None);
        Assert.IsTrue(ran, "and the thread the busy work had is free again");
    }

    // the thread is freed as soon as the work waiting for it is cancelled, before the semaphore has taken the cancelled wait off its list: it may hand the thread
    // to that wait, and the work is never run all the same.
    [TestMethod]
    public async Task Start_WorkCancelledWhileItWaits_AndAThreadFreedAtOnce_NeverRuns()
    {
        using var threads = new SemaphoreSlim(1, 1);
        using var running = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var busy = LargeStackThread.Start(() =>
        {
            running.Set();
            release.Wait();
        }, threads, CancellationToken.None);
        running.Wait();

        using var cancellation = new CancellationTokenSource();
        var ran = false;
        var waiting = LargeStackThread.Start(() => ran = true, threads, cancellation.Token);
        cancellation.Cancel();
        release.Set();

        await busy;
        await Assert.ThrowsAsync<OperationCanceledException>(() => waiting);
        Assert.IsFalse(ran, "it was cancelled before it had a thread");
    }
}
