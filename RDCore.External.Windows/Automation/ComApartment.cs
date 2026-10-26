using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace RDCore.External.Windows.Automation;

/// <summary>
/// The single-threaded apartment that the objects of automation servers, and the forms that host Microsoft Forms, live on: one thread, which runs the calls asked
/// of it in the order they are asked, and pumps the messages of its windows between them.
/// </summary>
/// <remarks>
/// <para>
/// A COM object written for a desktop expects to be called on the thread that created it, and an ActiveX control expects that thread to pump the messages of the
/// window it is in. So there is one such thread, whatever thread the pipeline runs a program on: a call is queued for it, and waited for. A call that is made on it - by
/// a handler of an event it raised, or by a call it is running - is run at once.
/// </para>
/// <para>
/// Between calls the thread waits for the next one or for a message, whichever comes first (<c>MsgWaitForMultipleObjectsEx</c>), so a form that is shown modeless
/// is drawn and answers its user when no program runs. While one does, the messages of the windows wait, as MS-VBA's do (<see cref="HoldsMessages"/>), until it
/// waits for them - <c>DoEvents</c>, a modal <c>Show</c> - or ends. A key goes to the active object of a form before it is dispatched, which is what moves the focus
/// with Tab.
/// The thread is aware of the DPI of each monitor (<c>DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2</c>): a window it makes is told its DPI, and when it changes.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class ComApartment : IDisposable
{
    // A program makes its calls one after the other, so the next one comes as soon as the last one is answered: the apartment looks for it for a moment before it
    // sleeps, so that a call that comes at once does not have to wake it.
    private static readonly TimeSpan Spin = TimeSpan.FromMicroseconds(250);

    private readonly ConcurrentQueue<Action> _work = new();
    private readonly ManualResetEvent _ready = new(false);
    private readonly Lazy<Thread> _thread;
    private readonly List<Func<Native.Message, bool>> _translators = [];
    private volatile bool _completed;
    private volatile bool _holding;
    private int _inCall;
    private int _modal;
    private IntPtr[]? _comWindows;

    /// <summary>
    /// Creates the apartment; its thread is started by the first call that needs it.
    /// </summary>
    public ComApartment()
    {
        _thread = new Lazy<Thread>(() =>
        {
            var thread = new Thread(Run) { IsBackground = true, Name = "RDCore automation apartment" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return thread;
        });
    }

    /// <summary>
    /// Whether the current thread is the apartment's.
    /// </summary>
    public bool IsCurrent => _thread.IsValueCreated && Environment.CurrentManagedThreadId == _thread.Value.ManagedThreadId;

    /// <summary>
    /// Whether a call the program waits for is being made on the apartment (<see cref="Counted"/>): an event an object of the apartment raises meanwhile is the answer
    /// to that call, and is handled inside it - a control's <c>Click</c> while a modal form's <c>Show</c> waits, an Excel event inside the call that raised it.
    /// </summary>
    public bool IsInCall => Volatile.Read(ref _inCall) > 0;

    /// <summary>
    /// Makes a call the program waits for, counted as one while it is made (<see cref="IsInCall"/>). Called on the apartment.
    /// </summary>
    /// <typeparam name="T">What the call returns.</typeparam>
    /// <param name="call">The call.</param>
    public T? Counted<T>(Func<T?> call)
    {
        _ = Interlocked.Increment(ref _inCall);
        try
        {
            return call();
        }
        finally
        {
            _ = Interlocked.Decrement(ref _inCall);
        }
    }

    /// <summary>
    /// Runs <paramref name="call"/> on the apartment and waits for it; on the apartment itself, it is run at once.
    /// </summary>
    /// <typeparam name="T">What the call returns.</typeparam>
    /// <param name="call">The call.</param>
    /// <returns>What it returned. What it threw is thrown here.</returns>
    public T? Invoke<T>(Func<T?> call)
    {
        _ = _thread.Value;
        if (IsCurrent)
        {
            return call();
        }

        T? result = default;
        Exception? failure = null;
        using var done = new ManualResetEventSlim();
        Post(() =>
        {
            try
            {
                result = call();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                done.Set();
            }
        });

        // most calls are answered at once: the caller looks for the answer for a moment before it sleeps, so that the apartment does not have to wake it.
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        while (!done.IsSet && System.Diagnostics.Stopwatch.GetElapsedTime(started) < Spin)
        {
            Thread.SpinWait(20);
        }

        done.Wait();
        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }

        return result;
    }

    /// <summary>
    /// Pumps the apartment - its messages, and the calls asked of it - until <paramref name="done"/> says to stop, as a modal window's loop does. Called on the
    /// apartment.
    /// </summary>
    /// <param name="done">Whether to stop; asked after each message and each call.</param>
    /// <exception cref="InvalidOperationException">The current thread is not the apartment's.</exception>
    public void PumpUntil(Func<bool> done)
    {
        if (!IsCurrent)
        {
            throw new InvalidOperationException("Only the apartment's own thread pumps it.");
        }

        // a modal window's loop is a program that waits: what was held is dispatched first, in the order it came.
        _modal++;
        try
        {
            Pump(() => done() || _completed);
        }
        finally
        {
            _modal--;
        }
    }

    /// <summary>
    /// Whether the messages of the apartment's windows wait - what their user does, what they draw, what Microsoft Forms posts to its own windows - because a program
    /// runs: MS-VBA dispatches none until the program waits for them (<c>DoEvents</c>, a modal <c>Show</c>) or ends (Excel-measured, <c>oracle\s41</c> E09, E12
    /// against E14, E15). What COM sends the apartment is dispatched all the same: a server's call back, an Excel event raised while the program runs.
    /// </summary>
    public bool HoldsMessages
    {
        get => _holding;
        set
        {
            _holding = value;

            // what was held is dispatched as soon as nothing runs.
            _ = _ready.Set();
        }
    }

    /// <summary>
    /// Dispatches every message that waits in the apartment's queue, held or not, as <c>DoEvents</c> does. Called on the apartment.
    /// </summary>
    /// <exception cref="InvalidOperationException">The current thread is not the apartment's.</exception>
    public void DispatchWaiting()
    {
        if (!IsCurrent)
        {
            throw new InvalidOperationException("Only the apartment's own thread pumps it.");
        }

        while (Native.PeekMessage(out var message, IntPtr.Zero, 0, 0, Native.PM_REMOVE))
        {
            Dispatch(message);
        }
    }

    /// <summary>
    /// Waits up to <paramref name="time"/> for a call asked of the apartment, and runs it: what the apartment does while an event it raised waits for the session to
    /// be open to it. Messages are not dispatched meanwhile, so that what the user does waits for the event too.
    /// </summary>
    /// <param name="time">How long to wait.</param>
    public void Serve(TimeSpan time)
    {
        _ = _ready.Reset();
        if (_work.IsEmpty)
        {
            // the runtime's wait on an apartment pumps what COM sends it, which is the calls a server makes back meanwhile.
            _ = _ready.WaitOne(time);
        }

        if (_work.TryDequeue(out var action))
        {
            action();
        }
    }

    /// <summary>
    /// Offers each key message to <paramref name="translator"/> before it is dispatched; one it says it has handled is not dispatched. Called on the apartment.
    /// </summary>
    /// <param name="translator">What is offered the key; it returns whether it handled it.</param>
    /// <returns>What stops offering it keys.</returns>
    internal IDisposable TranslateKeys(Func<Native.Message, bool> translator)
    {
        _translators.Add(translator);
        return new Removal(() => _translators.Remove(translator));
    }

    /// <summary>
    /// Raises an event of an object that lives on the apartment to whatever listens to the object's events: the events a
    /// form's window raises for it, which no connection point of the form does. Returns whether anything listens; set by the automation server. Called on the apartment.
    /// (<see cref="RDCore.External.Automation.IAutomationServer.Advise"/> is what listens.)
    /// </summary>
    internal Func<object, string, object?[], bool>? RaiseEvent { get; set; }

    /// <summary>
    /// Raised on the apartment when an object that lives on it is let go of by whatever held it, before it is released: what made more of it than the object - a
    /// form's window - lets go of that too.
    /// </summary>
    internal event Action<object>? Releasing;

    /// <summary>
    /// Says that <paramref name="released"/> is being let go of (<see cref="Releasing"/>). Called on the apartment.
    /// </summary>
    /// <param name="released">The object.</param>
    internal void OnReleasing(object released) => Releasing?.Invoke(released);

    /// <summary>
    /// Ends the apartment, once the calls that were asked of it are done. Every object that was made on it has been let go of by whatever held it.
    /// </summary>
    public void Dispose()
    {
        _completed = true;
        _ = _ready.Set();
    }

    private void Post(Action action)
    {
        _work.Enqueue(action);
        _ = _ready.Set();
    }

    private void Run()
    {
        // OLE, not only COM: an ActiveX control that is activated in place needs it.
        _ = Native.OleInitialize(IntPtr.Zero);
        _ = Native.SetThreadDpiAwarenessContext(Native.PerMonitorAwareV2);

        Pump(() => _completed && _work.IsEmpty);
    }

    private void Pump(Func<bool> done)
    {
        var handles = new[] { _ready.SafeWaitHandle.DangerousGetHandle() };
        while (!done())
        {
            if (_work.TryDequeue(out var action))
            {
                action();
                continue;
            }

            var holding = _holding && _modal == 0;
            if (holding ? TryPeekCom(out var message) : Native.PeekMessage(out message, IntPtr.Zero, 0, 0, Native.PM_REMOVE))
            {
                Dispatch(message);
                continue;
            }

            var idle = System.Diagnostics.Stopwatch.GetTimestamp();
            while (_work.IsEmpty && System.Diagnostics.Stopwatch.GetElapsedTime(idle) < Spin)
            {
                Thread.SpinWait(20);
            }

            // reset before looking again, so that a call queued in between sets it after, and the wait returns at once for it.
            _ = _ready.Reset();
            if (_work.IsEmpty && !done())
            {
                // what is held stays in the queue: the wait is for what comes next, not for what is there already (which would wake it at once, and again).
                // what came between the look and the reset is looked for once more, or it would be taken as seen and wake nothing.
                if (holding)
                {
                    _ = Native.GetQueueStatus(Native.QS_ALLINPUT);
                    if (TryPeekCom(out var late))
                    {
                        Dispatch(late);
                        continue;
                    }
                }

                _ = Native.MsgWaitForMultipleObjectsEx(1, handles, Native.Infinite, Native.QS_ALLINPUT, holding ? 0 : Native.MWMO_INPUTAVAILABLE);
            }
        }
    }

    // a message of COM's own windows (OleMainThreadWndClass, made when the thread entered its apartment): the calls of other apartments and processes, which a program
    // that runs answers too.
    private bool TryPeekCom(out Native.Message message)
    {
        if (_comWindows is not { Length: > 0 })
        {
            _comWindows = Native.ComWindowsOf(Native.GetCurrentThreadId());
        }

        foreach (var window in _comWindows)
        {
            if (Native.PeekMessage(out message, window, 0, 0, Native.PM_REMOVE))
            {
                return true;
            }
        }

        message = default;
        return false;
    }

    private void Dispatch(Native.Message message)
    {
        if (message.Kind is >= Native.WM_KEYFIRST and <= Native.WM_KEYLAST)
        {
            foreach (var translator in _translators.ToArray())
            {
                if (translator(message))
                {
                    return;
                }
            }
        }

        _ = Native.TranslateMessage(ref message);
        _ = Native.DispatchMessage(ref message);
    }

    private sealed class Removal(Action remove) : IDisposable
    {
        public void Dispose() => remove();
    }

    internal static class Native
    {
        public const uint PM_REMOVE = 0x0001;
        public const uint QS_ALLINPUT = 0x04FF;
        public const uint MWMO_INPUTAVAILABLE = 0x0004;
        public const uint Infinite = 0xFFFFFFFF;
        public const uint WM_KEYFIRST = 0x0100;
        public const uint WM_KEYLAST = 0x0109;
        public static readonly IntPtr PerMonitorAwareV2 = -4;

        [StructLayout(LayoutKind.Sequential)]
        public struct Message
        {
            public IntPtr Window;
            public uint Kind;
            public IntPtr WParam;
            public IntPtr LParam;
            public uint Time;
            public int X;
            public int Y;
        }

        [DllImport("ole32.dll")]
        public static extern int OleInitialize(IntPtr reserved);

        [DllImport("user32.dll")]
        public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

        // the Unicode ones: the ANSI ones would turn what the user types into the system's code page.
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PeekMessage(out Message message, IntPtr window, uint first, uint last, uint remove);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool TranslateMessage(ref Message message);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr DispatchMessage(ref Message message);

        [DllImport("user32.dll")]
        public static extern uint MsgWaitForMultipleObjectsEx(uint count, IntPtr[] handles, uint milliseconds, uint wakeMask, uint flags);

        [DllImport("user32.dll")]
        public static extern uint GetQueueStatus(uint flags);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        // COM's windows of a thread: message-only windows (HWND_MESSAGE) on today's Windows, top-level ones on older.
        public static IntPtr[] ComWindowsOf(uint thread)
        {
            const string comClass = "OleMainThreadWndClass";
            var found = new List<IntPtr>();
            foreach (var parent in new[] { MessageOnly, IntPtr.Zero })
            {
                var window = IntPtr.Zero;
                while ((window = FindWindowEx(parent, window, comClass, null)) != IntPtr.Zero)
                {
                    if (GetWindowThreadProcessId(window, out _) == thread)
                    {
                        found.Add(window);
                    }
                }
            }

            return [.. found];
        }

        private static readonly IntPtr MessageOnly = -3;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string? title);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    }
}
