using RDCore.SDK.Runtime.Abstract.StdLib;

namespace RDCore.SDK.Runtime.Abstract.Execution;

/// <summary>
/// The state of the standard library's <c>DateTime</c> module that outlives a call to it (<strong>MS-VBAL §6.1.2.4.2</strong>): the calendar its functions
/// work in, and the clock its properties read.
/// </summary>
/// <remarks>
/// <para>
/// The session is the program's <em>system</em>. Setting the system time (<c>Time = </c>, <strong>MS-VBAL §6.1.2.4.2.4</strong>) sets the time of this clock,
/// and never the machine's: from then on <c>Now</c>, <c>Time</c> and <c>Timer</c> read the time the program set, going on from it as the machine's clock does.
/// </para>
/// <para>
/// An <c>End</c> resets it with the rest of the session: the clock is the machine's again, and the calendar is Gregorian.
/// </para>
/// </remarks>
public interface ISessionClock
{
    /// <summary>
    /// The calendar the functions of the <c>DateTime</c> module work in (<strong>MS-VBAL §6.1.2.4.2.1</strong>); Gregorian until a program sets it.
    /// </summary>
    VBCalendar Calendar { get; set; }

    /// <summary>
    /// The current local date and time of the session: the machine's, unless a program set the time.
    /// </summary>
    DateTime Now { get; }

    /// <summary>
    /// Sets the time of day of the session's clock; the date stays what it is.
    /// </summary>
    /// <param name="timeOfDay">The time of day it is now, from 0:00:00 inclusive to 24:00:00 exclusive.</param>
    void SetTime(TimeSpan timeOfDay);

    /// <summary>
    /// The machine's clock and the Gregorian calendar again: the state of a session that no program has run in.
    /// </summary>
    void Reset();
}
