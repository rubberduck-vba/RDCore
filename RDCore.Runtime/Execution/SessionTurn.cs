using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Runtime.Execution;

/// <inheritdoc cref="ISessionTurn"/>
/// <remarks>
/// The turn is a baton. A program takes it when it starts and gives it up while it waits for a call, and an event takes it for as long as its handlers run; nobody runs without
/// it. What is not the baton is the rule for who may take it: a synchronous event may whenever it is free - which it is, because the program that made the call it was
/// raised by gave it up - and an asynchronous one only when the session is <see cref="IsOpenForEvents">open to them</see>, which is when no program runs or one pumps. Deciding
/// which an event is belongs to whatever received it, which knows whether a call was pending.
/// </remarks>
internal sealed class SessionTurn : ISessionTurn
{
    private readonly SemaphoreSlim _baton = new(1, 1);
    private int _programs;
    private int _pumping;
    private int _yielded;
    private int _waiting;
    private volatile bool _held;

    /// <inheritdoc/>
    public Func<IDisposable>? EventScope { get; set; }

    /// <inheritdoc/>
    public bool IsOpenForEvents => Volatile.Read(ref _programs) == 0 || Volatile.Read(ref _pumping) > 0 || Volatile.Read(ref _yielded) > 0;

    /// <inheritdoc/>
    public event Action<bool>? Running;

    /// <inheritdoc/>
    public bool IsRunning => Volatile.Read(ref _programs) > 0;

    /// <inheritdoc/>
    public void Enter()
    {
        _baton.Wait();
        _held = true;
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
            Release();
        }
    }

    /// <inheritdoc/>
    public IDisposable Yield()
    {
        if (!_held)
        {
            return NoScope.Instance;
        }

        // a program that waits for a call is a thread that receives the calls the server makes meanwhile: the events that waited are handled now.
        _ = Interlocked.Increment(ref _yielded);
        Release();
        return new Scope(() =>
        {
            Take();
            _ = Interlocked.Decrement(ref _yielded);
        });
    }

    /// <inheritdoc/>
    public void Pump()
    {
        // only what holds the turn can open it, and there is nothing to open it for when nothing waits.
        if (!_held || Volatile.Read(ref _waiting) == 0)
        {
            return;
        }

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
    public void RunEvent(Action handle)
    {
        // a program that runs prints where it prints; an event that nothing runs for prints where the host says.
        var scope = Volatile.Read(ref _programs) == 0 ? EventScope?.Invoke() : null;
        Take();
        try
        {
            handle();
        }
        finally
        {
            Release();
            scope?.Dispose();
        }
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
