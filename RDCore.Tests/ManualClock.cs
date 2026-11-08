namespace RDCore.Tests;

/// <summary>
/// A clock that moves only when a test moves it (<see cref="Advance"/>): a timer it makes fires once the clock is moved to its time, on the thread that moves
/// it, and never on its own. What is timed by it is as fast as the test says, whatever the machine.
/// </summary>
/// <remarks>
/// A timer fires once: a period is not kept. The time it tells - what a deadline is taken from - moves with it too, from <see cref="Start"/>.
/// </remarks>
internal sealed class ManualClock : TimeProvider
{
    /// <summary>The time the clock tells before it is moved.</summary>
    public static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly List<ManualTimer> _timers = [];
    private TimeSpan _elapsed;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_timers)
        {
            return Start + _elapsed;
        }
    }

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp()
    {
        lock (_timers)
        {
            return _elapsed.Ticks;
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        lock (_timers)
        {
            _timers.Add(timer);
        }

        timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>The timers that are to fire, and when: none when every timer has fired or is stopped.</summary>
    public int Pending
    {
        get
        {
            lock (_timers)
            {
                return _timers.Count(timer => timer.Due is not null);
            }
        }
    }

    /// <summary>Moves the clock on, and fires each timer whose time it reaches.</summary>
    public void Advance(TimeSpan by)
    {
        ManualTimer[] due;
        lock (_timers)
        {
            _elapsed += by;
            due = [.. _timers.Where(timer => timer.Due <= _elapsed)];
        }

        foreach (var timer in due)
        {
            timer.Fire();
        }
    }

    private sealed class ManualTimer(ManualClock clock, TimerCallback callback, object? state) : ITimer
    {
        public TimeSpan? Due { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (clock._timers)
            {
                Due = dueTime == Timeout.InfiniteTimeSpan ? null : clock._elapsed + dueTime;
            }

            return true;
        }

        public void Fire()
        {
            lock (clock._timers)
            {
                Due = null;
            }

            callback(state);
        }

        public void Dispose()
        {
            lock (clock._timers)
            {
                Due = null;
                clock._timers.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
