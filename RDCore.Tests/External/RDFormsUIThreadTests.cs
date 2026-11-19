using RDCore.External.Forms.RDForms;
using RDCore.SDK.Platform;

namespace RDCore.Tests.External;

/// <summary>
/// The thread the RDForms windows run their loop on: the process's main thread for a macOS desktop, whose windows run there only, and a thread of their own
/// everywhere else. A test thread plays the part of the process's main thread, and the loop is a stand-in for Avalonia's, which a process sets up once.
/// </summary>
[TestClass]
[TestCategory("External host")]
public sealed class RDFormsUIThreadTests
{
    // what a setup makes: the thread it ran on.
    private sealed record SetUp(Thread Thread);

    [TestMethod]
    [DataRow(RDFormsPlatform.Desktop, true, true, DisplayName = "a macOS desktop runs on the main thread")]
    [DataRow(RDFormsPlatform.Headless, true, false, DisplayName = "headless windows on macOS run on a thread of their own")]
    [DataRow(RDFormsPlatform.Desktop, false, false, DisplayName = "a Windows or Linux desktop runs on a thread of its own")]
    [DataRow(RDFormsPlatform.Headless, false, false, DisplayName = "headless windows elsewhere run on a thread of their own")]
    public void TheWindowsLoop_RunsWhereTheSystemRunsWindows(RDFormsPlatform platform, bool macOS, bool onTheMainThread)
        => Assert.AreEqual(onTheMainThread ? RDFormsUIThreadPlacement.MainThread : RDFormsUIThreadPlacement.OwnThread, RDFormsUIThread.PlacementOf(platform, macOS));

    // runs a main thread on a thread of the test's own, which plays the part of the process's, and answers what it returned and the thread it was.
    private static (int Result, Thread Main) Serve(Func<ProcessMainThread, Task<int>> program)
    {
        var main = new ProcessMainThread();
        var result = -1;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = main.Run(() => program(main));
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });

        thread.Start();
        thread.Join();
        return failure is null ? (result, thread) : throw failure;
    }

    // the loop runs on the main thread for as long as the program does, and the main thread returns the program's exit code once it ends.
    [TestMethod]
    public void OnTheMainThread_TheLoopIsSetUpAndRunThere_AndEndsWithTheProgram()
    {
        SetUp? started = null;
        Thread? looped = null;

        var (result, main) = Serve(served =>
        {
            started = RDFormsUIThread.Start(RDFormsUIThreadPlacement.MainThread, served, () => new SetUp(Thread.CurrentThread), (_, token) =>
            {
                looped = Thread.CurrentThread;
                token.WaitHandle.WaitOne();
            });

            return Task.FromResult(0);
        });

        Assert.AreEqual(0, result);
        Assert.AreSame(main, started?.Thread);
        Assert.AreSame(main, looped);
    }

    // a desktop that cannot be set up (no window server) fails the start, runs no loop, and leaves the main thread to the program, which ends as any other does.
    [TestMethod]
    public void OnTheMainThread_ASetupThatFails_FailsTheStart_AndTheProgramEnds()
    {
        Exception? failure = null;
        var looped = false;

        var (result, _) = Serve(served =>
        {
            failure = Assert.ThrowsExactly<InvalidOperationException>(() => RDFormsUIThread.Start<SetUp>(RDFormsUIThreadPlacement.MainThread, served,
                () => throw new InvalidOperationException("no window server"), (_, _) => looped = true));
            return Task.FromResult(0);
        });

        Assert.AreEqual(0, result);
        Assert.AreEqual("no window server", failure?.Message);
        Assert.IsFalse(looped);
    }

    // a process whose entry point serves no main thread cannot have windows that run there only: the start fails before anything is set up.
    [TestMethod]
    public void OnTheMainThread_WithNoMainThreadServed_FailsBeforeTheSetup()
    {
        var setUp = false;

        var failure = Assert.ThrowsExactly<InvalidOperationException>(() => RDFormsUIThread.Start(RDFormsUIThreadPlacement.MainThread, main: null, () =>
        {
            setUp = true;
            return new SetUp(Thread.CurrentThread);
        }, (_, _) => { }));

        StringAssert.Contains(failure.Message, "main thread is not served");
        Assert.IsFalse(setUp);
    }

    [TestMethod]
    public void OnAThreadOfItsOwn_TheLoopIsSetUpAndRunThere()
    {
        Thread? looped = null;
        (string? Name, bool IsBackground) thread = default;
        using var looping = new ManualResetEventSlim();

        // the thread is read as it runs: once the loop returns, it ends.
        var started = RDFormsUIThread.Start(RDFormsUIThreadPlacement.OwnThread, main: null, () =>
        {
            thread = (Thread.CurrentThread.Name, Thread.CurrentThread.IsBackground);
            return new SetUp(Thread.CurrentThread);
        }, (_, _) =>
        {
            looped = Thread.CurrentThread;
            looping.Set();
        });

        looping.Wait();
        Assert.AreNotSame(Thread.CurrentThread, started.Thread);
        Assert.AreSame(started.Thread, looped);
        Assert.AreEqual(("RDForms UI", true), thread);
    }

    [TestMethod]
    public void OnAThreadOfItsOwn_ASetupThatFails_FailsTheStart()
    {
        var looped = false;

        var failure = Assert.ThrowsExactly<InvalidOperationException>(() => RDFormsUIThread.Start<SetUp>(RDFormsUIThreadPlacement.OwnThread, main: null,
            () => throw new InvalidOperationException("no display"), (_, _) => looped = true));

        Assert.AreEqual("no display", failure.Message);
        Assert.IsFalse(looped);
    }
}
