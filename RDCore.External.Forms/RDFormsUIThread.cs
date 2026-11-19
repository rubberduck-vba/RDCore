using RDCore.SDK.Platform;
using System.Runtime.ExceptionServices;

namespace RDCore.External.Forms.RDForms;

// the thread the RDForms windows run their loop on.
internal enum RDFormsUIThreadPlacement
{
    // a thread of RDForms' own, made for the loop.
    OwnThread,

    // the process's main thread, which its entry point serves (ProcessMainThread).
    MainThread,
}

// where the windows' loop runs, and how it is started there. Avalonia sets its platform up on one thread, and that thread runs its loop.
internal static class RDFormsUIThread
{
    // macOS runs a window's loop on the process's main thread only (Avalonia.Native refuses a dispatcher on any other). The windows of Windows and Linux, and
    // headless ones on every system, run on any thread, and get one of RDForms' own: the program's session keeps whatever thread its host gave it.
    public static RDFormsUIThreadPlacement PlacementOf(RDFormsPlatform platform, bool macOS)
        => platform == RDFormsPlatform.Desktop && macOS ? RDFormsUIThreadPlacement.MainThread : RDFormsUIThreadPlacement.OwnThread;

    // sets the loop up on the thread of the placement, then runs it there, and answers what the setup made once it is made. What the setup throws is thrown
    // here, and the loop is not run. A loop on the main thread returns when the program ends; one on a thread of its own runs until the process ends.
    public static T Start<T>(RDFormsUIThreadPlacement placement, ProcessMainThread? main, Func<T> setUp, Action<T, CancellationToken> loop)
        where T : class
        => placement == RDFormsUIThreadPlacement.MainThread
            ? StartOnTheMainThread(main ?? throw new InvalidOperationException(
                "The process's main thread is not served: its entry point does not give it to the windows, which this system runs on it only."), setUp, loop)
            : StartOnAThreadOfItsOwn(setUp, loop);

    private static T StartOnTheMainThread<T>(ProcessMainThread main, Func<T> setUp, Action<T, CancellationToken> loop)
        where T : class
    {
        T? started = null;
        Exception? failure = null;
        using var ready = new ManualResetEventSlim();
        try
        {
            _ = main.PostAsync(token =>
            {
                try
                {
                    started = setUp();
                }
                catch (Exception exception)
                {
                    // the main thread goes back to serving the program, which can end as any other does.
                    failure = exception;
                    return;
                }
                finally
                {
                    ready.Set();
                }

                loop(started, token);
            });
        }
        catch (InvalidOperationException)
        {
            throw new InvalidOperationException("The program is ending: the process's main thread runs no more work.");
        }

        ready.Wait();
        return Made(started, failure);
    }

    // what the setup made, or what it threw, with the stack it threw it with.
    private static T Made<T>(T? started, Exception? failure)
        where T : class
    {
        if (failure is not null)
        {
            ExceptionDispatchInfo.Throw(failure);
        }

        return started!;
    }

    private static T StartOnAThreadOfItsOwn<T>(Func<T> setUp, Action<T, CancellationToken> loop)
        where T : class
    {
        T? started = null;
        Exception? failure = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            try
            {
                started = setUp();
            }
            catch (Exception exception)
            {
                failure = exception;
                return;
            }
            finally
            {
                ready.Set();
            }

            loop(started, CancellationToken.None);
        })
        {
            IsBackground = true,
            Name = "RDForms UI",
        };

        if (OperatingSystem.IsWindows())
        {
            // the desktop's drag and drop, clipboard and accessibility need a single-threaded apartment on Windows.
            thread.SetApartmentState(ApartmentState.STA);
        }

        thread.Start();
        ready.Wait();
        return Made(started, failure);
    }
}
