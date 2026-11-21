using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using RDCore.External.Automation;
using RDCore.Forms.Avalonia;
using RDCore.Forms.Avalonia.Rendering;
using RDCore.SDK.Platform;
using System.Globalization;

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

// the one Avalonia application of the process, on its UI thread: Avalonia allows one UI thread to a process, and the thread that sets it up is that thread.
// It is a thread of its own, or the process's main thread on macOS (RDFormsUIThread). Every call on an RDForms object is made there, inline when it is made
// there already, as the calls an event's handlers make are.
internal static class RDFormsUI
{
    private const int Failed = unchecked((int)0x80004005);

    // windows that cannot be started are a form the platform cannot create, as on a platform with no form host: REGDB_E_CLASSNOTREG, which the call that
    // creates the form (New, or the program's first use of it) reports as 429.
    private const int CannotStart = unchecked((int)0x80040154);

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
    // The first start names the application too, with the name of the program it starts for (RDForms' own when it is blank), before the platform is set
    // up, as macOS names its application menu as it sets up. A start after one that failed fails as it did, on either platform.
    public static void Start(RDFormsPlatform platform, string applicationName)
    {
        lock (Gate)
        {
            if (_failure is not null)
            {
                throw new AutomationException(CannotStart, string.Format(CultureInfo.CurrentCulture, ExternalMessages.RDFormsCannotStart, _failure));
            }

            if (_dispatcher is not null)
            {
                // Avalonia sets up one windowing platform per process, for good, so the windows cannot switch. No entry point mixes the two: a process
                // runs its programs all unattended or all attended (--unattended, or the client's Unattended capability), and has one external host.
                if (platform != _platform)
                {
                    throw new AutomationException(Failed, _platform == RDFormsPlatform.Headless
                        ? ExternalMessages.RDFormsWindowsAreHeadless
                        : ExternalMessages.RDFormsWindowsAreOnTheDesktop);
                }

                return;
            }

            Dispatcher started;
            try
            {
                started = RDFormsUIThread.Start(RDFormsUIThread.PlacementOf(platform, OperatingSystem.IsMacOS()), ProcessMainThread.Current, () =>
                {
                    BuilderFor(platform, applicationName).SetupWithoutStarting();
                    var own = Dispatcher.UIThread;
                    return own.CheckAccess() ? own : throw new InvalidOperationException("Avalonia's UI thread was set up on another thread of this process.");
                }, (own, token) => own.MainLoop(token));
            }
            catch (Exception failure)
            {
                _failure = failure.Message;
                throw new AutomationException(CannotStart, string.Format(CultureInfo.CurrentCulture, ExternalMessages.RDFormsCannotStart, _failure));
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
    public static void StartUnattended(string applicationName)
    {
        lock (Gate)
        {
            if (_dispatcher is null)
            {
                Start(RDFormsPlatform.Headless, applicationName);
            }
        }
    }

    // the name of the application of the windows: the program's, or RDForms' own when the program's is blank. Read and renamed on the UI thread; a rename
    // shows where the platform reads the name again.
    public static string ApplicationName
    {
        get => Application.Current?.Name ?? string.Empty;
        set => Application.Current!.Name = string.IsNullOrWhiteSpace(value) ? RDFormsApplication.DefaultName : value;
    }

    // RDForms' own application, which its controls' look is made for, named for the program, on the platform asked for.
    private static AppBuilder BuilderFor(RDFormsPlatform platform, string applicationName)
    {
        var named = RDFormsApplication.Configure(applicationName);
        return platform switch
        {
            RDFormsPlatform.Headless => named.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }),
            _ => named.UsePlatformDetect(),
        };
    }

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
