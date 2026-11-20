using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using RDCore.External.Automation;
using RDCore.Forms.Avalonia;
using RDCore.Forms.Avalonia.Rendering;

namespace RDCore.External.Forms.RDForms;

/// <summary>
/// Where the RDForms windows of a process run: on a desktop, or headless, with no window anyone can see.
/// </summary>
public enum RDFormsPlatform
{
    /// <summary>
    /// The windows are the desktop's: Windows, X11 or macOS, whichever the process runs on.
    /// </summary>
    Desktop,

    /// <summary>
    /// The windows are drawn and laid out as on a desktop, but are not shown on any screen, and only code can use them.
    /// </summary>
    Headless,
}

// the one Avalonia application of the process, on a thread of its own: Avalonia allows one UI thread to a process, and the thread that sets it up is that thread.
// Every call on an RDForms object is made there, inline when it is made there already, as the calls an event's handlers make are.
internal static class RDFormsUI
{
    private const int Failed = unchecked((int)0x80004005);

    private static readonly object Gate = new();
    private static Dispatcher? _dispatcher;
    private static RDFormsPlatform _platform;

    // why the windows could not be started, which every later start of the process is answered with. Avalonia's setup is made once per process
    // (AppBuilder.Setup marks it made before it begins), so one that failed cannot be made again, whatever changed since. Touched under Gate.
    private static string? _failure;

    // the programs whose calls the UI thread is making, the innermost last. An event a form raises while the thread makes a call of the form's own program
    // is the answer to that call; an event of another program's form is not, since that program is waiting on no call. Every program of the process shares
    // the thread. Touched on the UI thread only.
    private static readonly List<RDFormsProgram> Callers = [];

    // how many form hosts hold the windows' messages because a program of theirs runs. While one does, the UI thread makes the programs' calls and dispatches
    // nothing else, as a VB6 program's own thread does: what a window posts, such as the focus SetFocus moves, waits until the program waits for it (DoEvents, a
    // modal Show) or stops. Touched under Gate.
    private static int _holders;
    private static volatile bool _holding;

    // wakes the held UI thread for a call, or for the end of the hold.
    private static readonly SemaphoreSlim Arrived = new(0);

    // whether the UI thread is in its hold, and whether a hold asked for during a call waits for that call to be answered. Touched on the UI thread only.
    private static bool _inHold;
    private static bool _holdAfterCall;

    // whether the UI thread is making a call of `program`, which is what makes an event of that program's forms synchronous.
    public static bool IsInCallOf(RDFormsProgram program) => Callers.Contains(program);

    public static Dispatcher Dispatcher => _dispatcher ?? throw new InvalidOperationException("The RDForms UI thread is not started.");

    // whether the UI thread is started: before it is, the process has no RDForms window. Read under the gate Start writes it under.
    public static bool IsStarted
    {
        get
        {
            lock (Gate)
            {
                return _dispatcher is not null;
            }
        }
    }

    // starts the UI thread on the platform asked for, once per process: the first start says which, and a later one that asks for the other cannot have it.
    // A start after one that failed fails as it did, on either platform.
    public static void Start(RDFormsPlatform platform)
    {
        lock (Gate)
        {
            if (_failure is not null)
            {
                throw new AutomationException(Failed, _failure);
            }

            if (_dispatcher is not null)
            {
                if (platform != _platform)
                {
                    throw new AutomationException(Failed, $"The RDForms windows of this process are {_platform}, and cannot be {platform} as well.");
                }

                return;
            }

            Exception? failure = null;
            Dispatcher? started = null;
            using var ready = new ManualResetEventSlim();
            var thread = new Thread(() =>
            {
                Dispatcher own;
                try
                {
                    BuilderFor(platform).SetupWithoutStarting();
                    own = Dispatcher.UIThread;
                    if (!own.CheckAccess())
                    {
                        throw new InvalidOperationException("Avalonia's UI thread was set up on another thread of this process.");
                    }

                    started = own;
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

                own.MainLoop(CancellationToken.None);
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
            if (failure is not null)
            {
                _failure = $"The RDForms windows cannot be started: {failure.Message}";
                throw new AutomationException(Failed, _failure);
            }

            _platform = platform;
            _dispatcher = started;
            if (_holding)
            {
                started!.Post(HoldMessages, DispatcherPriority.Send);
            }
        }
    }

    // starts the UI thread for a program run unattended, which shows no window: the windows the process has, whichever they are, or headless ones, which need
    // no screen, when it has none yet.
    public static void StartUnattended()
    {
        lock (Gate)
        {
            if (_dispatcher is null)
            {
                Start(RDFormsPlatform.Headless);
            }
        }
    }

    // RDForms' own application, which its controls' look is made for, on the platform asked for.
    private static AppBuilder BuilderFor(RDFormsPlatform platform) => platform switch
    {
        RDFormsPlatform.Headless => RDFormsApplication.Configure().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }),
        _ => RDFormsApplication.Configure().UsePlatformDetect(),
    };

    // makes a call of `program` on the UI thread, and answers what it returned: the program is the one the call is made of, whose forms' events are the
    // answer to it. What fails there fails the call, as a server's failure does.
    public static T Run<T>(RDFormsProgram program, Func<T> call)
    {
        var dispatcher = Dispatcher;
        if (dispatcher.CheckAccess())
        {
            return Make(program, call);
        }

        var operation = dispatcher.InvokeAsync(() => Make(program, call), DispatcherPriority.Send);
        if (_holding)
        {
            _ = Arrived.Release();
        }

        // the call completes on the UI thread, which is never the thread that waits for it.
#pragma warning disable VSTHRD002
        return operation.GetTask().GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
    }

    public static void Run(RDFormsProgram program, Action call) => Run(program, () =>
    {
        call();
        return true;
    });

    private static T Make<T>(RDFormsProgram program, Func<T> call)
    {
        Callers.Add(program);
        try
        {
            return call();
        }
        catch (Exception exception) when (exception is not AutomationException)
        {
            throw new AutomationException(Failed, exception.Message);
        }
        finally
        {
            Callers.RemoveAt(Callers.Count - 1);
            if (Callers.Count == 0 && _holdAfterCall)
            {
                _holdAfterCall = false;
                Dispatcher.Post(HoldMessages, DispatcherPriority.Send);
            }
        }
    }

    // does the work that waits on the UI thread, held or not: what a DoEvents dispatches.
    public static void Pump() => Dispatcher.RunJobs(null);

    // what an event that waits for its handlers lets through meanwhile: their calls, and while a program runs nothing else.
    public static void Serve() => Dispatcher.RunJobs(_holding ? DispatcherPriority.Send : null);

    // a form host's program starts or stops running.
    public static void Hold(bool running)
    {
        lock (Gate)
        {
            _holders += running ? 1 : -1;
            var holding = _holders > 0;
            if (holding == _holding)
            {
                return;
            }

            _holding = holding;
            if (holding)
            {
                _dispatcher?.Post(HoldMessages, DispatcherPriority.Send);
            }
            else
            {
                _ = Arrived.Release();
            }
        }
    }

    // on the UI thread: makes the programs' calls, and runs no other job, until no program runs. A hold asked for while a call is being made begins once it is
    // answered, since what the call waits on serves the calls meanwhile.
    private static void HoldMessages()
    {
        if (_inHold || !_holding)
        {
            return;
        }

        if (Callers.Count > 0)
        {
            _holdAfterCall = true;
            return;
        }

        _inHold = true;
        try
        {
            while (_holding)
            {
                Dispatcher.RunJobs(DispatcherPriority.Send);
                if (_holding)
                {
                    Arrived.Wait();
                }
            }
        }
        finally
        {
            _inHold = false;
        }
    }
}
