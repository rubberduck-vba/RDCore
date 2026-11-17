using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Runtime.Execution;

/// <inheritdoc cref="ISessionTurn"/>
/// <remarks>
/// The turn is a baton. A program takes it when it starts and gives it up while it waits for a call, and an event takes it for as long as its handlers run; nobody runs without
/// it. What is not the baton is the rule for who may take it: a synchronous event may whenever it is free - which it is, because the program that made the call it was
/// raised by gave it up - and an asynchronous one only when the session is <see cref="IsOpenForEvents">open to them</see>, which is when no program runs or one pumps. Deciding
/// which an event is belongs to whatever received it, which knows whether a call was pending.
/// <para>
/// Who a thread runs code for is the turn's to know as well: the program, an event of the program, an event of the session, or code beside a program that waits at a
/// stop. An event is the program's when the thread that gave the turn up for it was the program's or an event of the program's, and the code's beside the stop when that
/// code gave it up. One that takes the turn while no program runs is the session's, and so is one its handlers' calls raise.
/// </para>
/// </remarks>
internal sealed class SessionTurn : ISessionTurn
{
    // who the code a thread runs is for, as far as the turn knows.
    private enum Role
    {
        None,
        Program,
        ProgramEvent,
        SessionEvent,
        Alongside,
        AlongsideEvent,
    }

    // how long an event of the program that waits for a stop to be over serves its thread before it looks again.
    private static readonly TimeSpan HeldServe = TimeSpan.FromMilliseconds(10);

    private readonly SemaphoreSlim _baton = new(1, 1);
    private readonly ThreadLocal<Role> _role = new();
    private readonly ThreadLocal<Action<TimeSpan>?> _serve = new();
    private int _programs;
    private int _pumping;
    private int _yielded;
    private int _waiting;
    private int _stopped;
    private long _heldArrived;
    private long _heldHandled;
    private volatile bool _held;

    // who the thread that last gave the turn up runs code for: what the events that take it meanwhile are.
    private volatile int _yielder;

    // the thread that waits at the stop, and whether it gave the turn up to wait; and the one that takes the turn back first once the stop is over, before an
    // event that waited for it.
    private volatile Thread? _stoppedThread;
    private volatile bool _stoppedYielded;
    private volatile Thread? _resuming;

    /// <inheritdoc/>
    public Func<IDisposable>? EventScope { get; set; }

    /// <inheritdoc/>
    public bool IsOpenForEvents => Volatile.Read(ref _stopped) == 0
        && (Volatile.Read(ref _programs) == 0 || Volatile.Read(ref _pumping) > 0 || Volatile.Read(ref _yielded) > 0);

    /// <inheritdoc/>
    public event Action<bool>? Running;

    /// <inheritdoc/>
    public bool IsRunning => Volatile.Read(ref _programs) > 0;

    /// <inheritdoc/>
    public bool RunsAnEventOfTheProgram => _role.Value is Role.ProgramEvent && IsRunning;

    /// <inheritdoc/>
    public bool RunsAnEventOfTheSession => _role.Value is Role.SessionEvent;

    /// <summary>
    /// Raised on the thread of an event of the session (<see cref="RunsAnEventOfTheSession"/>) once its handlers are done, and the turn is given back: what a
    /// debugger that resumed one of them stopped answers by.
    /// </summary>
    internal event Action<Thread>? SessionEventHandled;

    /// <inheritdoc/>
    public Action<TimeSpan>? ServeOfTheCallingThread => _serve.Value;

    /// <inheritdoc/>
    public void Enter()
    {
        _baton.Wait();
        _held = true;
        _role.Value = Role.Program;
        if (Interlocked.Increment(ref _programs) != 1)
        {
            return;
        }

        // what is told fails - a host of windows that stopped - and the program does not start: the turn is given back, or nothing ever takes it again.
        try
        {
            Running?.Invoke(true);
        }
        catch
        {
            _ = Interlocked.Decrement(ref _programs);
            _role.Value = Role.None;
            Release();
            throw;
        }
    }

    /// <inheritdoc/>
    public void Exit()
    {
        // the turn is given back whatever being told the program ended does, or nothing ever takes it again.
        try
        {
            if (Interlocked.Decrement(ref _programs) == 0)
            {
                Running?.Invoke(false);
            }
        }
        finally
        {
            if (_role.Value is Role.Program)
            {
                _role.Value = Role.None;
            }

            Release();
        }
    }

    /// <inheritdoc/>
    public IDisposable Yield()
    {
        var role = _role.Value;
        if (!_held)
        {
            // code beside a stop gives up a turn nobody holds - the thread that waits there gave it up - and what its call raises is still its own.
            return role is Role.Alongside or Role.AlongsideEvent ? Yielding(role) : NoScope.Instance;
        }

        // a program that waits for a call is a thread that receives the calls the server makes meanwhile: the events that waited are handled now.
        var yielding = Yielding(role);
        var thread = Thread.CurrentThread;
        var waitsAtTheStop = thread == _stoppedThread;
        if (waitsAtTheStop)
        {
            _stoppedYielded = true;
        }

        _ = Interlocked.Increment(ref _yielded);
        Release();
        return new Scope(() =>
        {
            Take();
            _ = Interlocked.Decrement(ref _yielded);
            yielding.Dispose();
            if (waitsAtTheStop)
            {
                _ = Interlocked.CompareExchange(ref _resuming, null, thread);
            }
        });
    }

    // the events that take the turn while the scope is not disposed are those of the code `role` says.
    private Scope Yielding(Role role)
    {
        var previous = _yielder;
        _yielder = (int)role;
        return new Scope(() => _yielder = previous);
    }

    /// <inheritdoc/>
    public void Pump()
    {
        // only what holds the turn can open it, and there is nothing to open it for when nothing waits.
        if (!_held || Volatile.Read(ref _waiting) == 0)
        {
            return;
        }

        using var yielding = Yielding(_role.Value);
        _ = Interlocked.Increment(ref _pumping);
        try
        {
            Release();

            // the events that waited take their turns, and each says it is done by no longer waiting.
            SpinWait.SpinUntil(() => Volatile.Read(ref _waiting) == 0);
        }
        finally
        {
            Take();
            _ = Interlocked.Decrement(ref _pumping);
        }
    }

    /// <inheritdoc/>
    public IDisposable Waiting()
    {
        _ = Interlocked.Increment(ref _waiting);
        return new Scope(() => Interlocked.Decrement(ref _waiting));
    }

    /// <inheritdoc/>
    public void RunEvent(Action handle) => Run(handle, null);

    /// <inheritdoc/>
    public void RunEvent(Action handle, Action<TimeSpan> serve) => Run(handle, serve);

    private void Run(Action handle, Action<TimeSpan>? serve)
    {
        // an event that a call of code beside a stop raised is that code's; one with no program running is the session's; any other is the program's. None of the
        // program's or the session's runs while a handler waits at a stop.
        var role = (Role)_yielder is Role.Alongside or Role.AlongsideEvent ? Role.AlongsideEvent
            : Volatile.Read(ref _programs) == 0 ? Role.SessionEvent
            : Role.ProgramEvent;
        var held = role is Role.ProgramEvent or Role.SessionEvent
            && (Volatile.Read(ref _stopped) > 0 || _resuming is { } resuming && resuming != Thread.CurrentThread);
        if (held)
        {
            WaitForTheStopToBeOver(serve);
        }

        try
        {
            // a program that runs prints where it prints; an event that nothing runs for prints where the host says.
            var scope = Volatile.Read(ref _programs) == 0 ? EventScope?.Invoke() : null;
            Take();
            var (previousRole, previousServe) = (_role.Value, _serve.Value);
            (_role.Value, _serve.Value) = (role, serve);
            try
            {
                handle();
            }
            finally
            {
                (_role.Value, _serve.Value) = (previousRole, previousServe);
                Release();
                scope?.Dispose();
                if (role is Role.SessionEvent && previousRole is not Role.SessionEvent)
                {
                    SessionEventHandled?.Invoke(Thread.CurrentThread);
                }
            }
        }
        finally
        {
            if (held)
            {
                _ = Interlocked.Increment(ref _heldHandled);
            }
        }
    }

    // an event of the program that came while the program waits at a stop waits for it to be resumed, and for the thread that waited there to have the turn back,
    // serving its thread meanwhile; the events that waited are handled in the order they came. One that came on the thread that waits at the stop runs above it, and
    // so first.
    private void WaitForTheStopToBeOver(Action<TimeSpan>? serve)
    {
        var ticket = Interlocked.Increment(ref _heldArrived);
        var thread = Thread.CurrentThread;
        var spin = new SpinWait();
        while (Volatile.Read(ref _stopped) > 0 || _resuming is { } resuming && resuming != thread || Volatile.Read(ref _heldHandled) != ticket - 1)
        {
            if (serve is not null)
            {
                serve(HeldServe);
            }
            else
            {
                spin.SpinOnce();
            }
        }
    }

    /// <inheritdoc/>
    public IDisposable Stopped()
    {
        _stoppedThread = Thread.CurrentThread;
        _stoppedYielded = false;
        _ = Interlocked.Increment(ref _stopped);
        return new Scope(() =>
        {
            // a thread that gave the turn up to wait takes it back before the events that waited: they run after the code it goes on with.
            if (_stoppedYielded)
            {
                _resuming = _stoppedThread;
            }

            _stoppedThread = null;
            _stoppedYielded = false;
            _ = Interlocked.Decrement(ref _stopped);
        });
    }

    /// <inheritdoc/>
    public IDisposable Alongside()
    {
        var previous = _role.Value;
        _role.Value = Role.Alongside;
        return new Scope(() => _role.Value = previous);
    }

    private void Take()
    {
        _baton.Wait();
        _held = true;
    }

    private void Release()
    {
        _held = false;
        _ = _baton.Release();
    }

    private sealed class Scope(Action dispose) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                dispose();
            }
        }
    }

    private sealed class NoScope : IDisposable
    {
        public static readonly NoScope Instance = new();

        public void Dispose()
        {
        }
    }
}
