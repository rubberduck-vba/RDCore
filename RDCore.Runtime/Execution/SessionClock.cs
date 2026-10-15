using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Abstract.StdLib;

namespace RDCore.Runtime.Execution;

/// <inheritdoc cref="ISessionClock"/>
/// <param name="source">The machine's clock: what the session's clock reads when no program has set the time, and goes on from when one has.</param>
internal sealed class SessionClock(TimeProvider source) : ISessionClock
{
    // how far the program set the session's clock from the machine's: the clock goes on as the machine's does, the same distance from it.
    private TimeSpan _offset;

    /// <summary>
    /// The machine's clock; a test gives the session one that it controls.
    /// </summary>
    internal TimeProvider Source { get; set; } = source;

    public VBCalendar Calendar { get; set; }

    public DateTime Now => Source.GetLocalNow().DateTime + _offset;

    public void SetTime(TimeSpan timeOfDay)
    {
        var machine = Source.GetLocalNow().DateTime;
        _offset = machine.Date + timeOfDay - machine;
    }

    public void Reset()
    {
        _offset = TimeSpan.Zero;
        Calendar = VBCalendar.VBCalGreg;
    }
}
