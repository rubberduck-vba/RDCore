using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.SDK.Runtime.Abstract.StdLib;

/// <summary>
/// <strong>MS-VBAL 6.1.2.4 DateTime Module</strong>
/// </summary>
/// <remarks>
/// Formalizes the public interface of the standard library <c>VBA.DateTime</c> module.
/// <para>
/// A function the specification declares with no <c>As</c> clause returns a <c>Variant</c>, which holds the type its description says: <c>DateDiff</c> a
/// <c>Long</c>, <c>DatePart</c> and the date-part functions an <c>Integer</c>, <c>DateAdd</c>, <c>DateSerial</c> and <c>TimeSerial</c> a <c>Date</c>.
/// The functions work in the calendar the <see cref="Calendar()"/> property says, and the properties read the session's clock
/// (<see cref="ISessionClock"/>).
/// </para>
/// </remarks>
[StdLibModule]
public interface IStdDateTimeModule
{
    /// <summary>
    /// The valid <c>Interval</c> arguments of <see cref="DateAdd"/>, <see cref="DateDiff"/> and <see cref="DatePart"/>; compared without regard to case.
    /// </summary>
    /// <remarks>
    /// 💥 Any other value raises run-time error 5 <see cref="VBRuntimeErrorId.InvalidProcedureCallOrArgument"/>.
    /// </remarks>
    public static class Intervals
    {
        /// <summary>Year.</summary>
        public const string Year = "yyyy";
        /// <summary>Quarter.</summary>
        public const string Quarter = "q";
        /// <summary>Month.</summary>
        public const string Month = "m";
        /// <summary>Day of year.</summary>
        public const string DayOfYear = "y";
        /// <summary>Day.</summary>
        public const string Day = "d";
        /// <summary>Weekday.</summary>
        public const string Weekday = "w";
        /// <summary>Week.</summary>
        public const string Week = "ww";
        /// <summary>Hour.</summary>
        public const string Hour = "h";
        /// <summary>Minute.</summary>
        public const string Minute = "n";
        /// <summary>Second.</summary>
        public const string Second = "s";
    }

    #region 6.1.2.4.1 Public Functions
    /// <summary>
    /// <strong>MS-VBAL 6.1.2.4.1.1 DateAdd</strong> Gets a date to which a number of intervals of time is added.
    /// </summary>
    /// <remarks>
    /// The result is never an invalid date: one month after January 31 is the last day of February. A result before the year 100 raises error 5.
    /// </remarks>
    /// <param name="interval">The interval of time to add (<see cref="Intervals"/>).</param>
    /// <param name="number">How many intervals to add: positive for the future, negative for the past; rounded to the nearest whole number.</param>
    /// <param name="date">The date the intervals are added to.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBVariantValue> DateAdd(VBStringValue interval, VBDoubleValue number, VBVariantValue date);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.4.1.2 DateDiff</strong> Gets the number of intervals of time between two dates.
    /// </summary>
    /// <remarks>
    /// The count is of the boundaries of the interval between the two: from December 31 to the January 1 after it is one year. A week (<c>"w"</c>) is counted
    /// on the day of the week <paramref name="date1"/> falls on, and a calendar week (<c>"ww"</c>) on <paramref name="firstDayOfWeek"/>; either counts
    /// <paramref name="date2"/> and not <paramref name="date1"/>. Negative when <paramref name="date1"/> is the later of the two.
    /// </remarks>
    /// <param name="interval">The interval of time to count (<see cref="Intervals"/>).</param>
    /// <param name="date1">The date counted from.</param>
    /// <param name="date2">The date counted to.</param>
    /// <param name="firstDayOfWeek">The first day of the week. Sunday when unspecified.</param>
    /// <param name="firstWeekOfYear">The first week of the year. The week of January 1 when unspecified.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBVariantValue> DateDiff(
        VBStringValue interval, VBVariantValue date1, VBVariantValue date2,
        VBDayOfWeek firstDayOfWeek = VBDayOfWeek.VBSunday, VBFirstWeekOfYear firstWeekOfYear = VBFirstWeekOfYear.VBFirstJan1);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.4.1.3 DatePart</strong> Gets one part of a date, in the units of the current calendar.
    /// </summary>
    /// <param name="interval">The part of the date to get (<see cref="Intervals"/>).</param>
    /// <param name="baseDate">The date the part is taken from.</param>
    /// <param name="firstDayOfWeek">The first day of the week. Sunday when unspecified.</param>
    /// <param name="firstWeekOfYear">The first week of the year. The week of January 1 when unspecified.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBVariantValue> DatePart(
        VBStringValue interval, VBVariantValue baseDate,
        VBDayOfWeek firstDayOfWeek = VBDayOfWeek.VBSunday, VBFirstWeekOfYear firstWeekOfYear = VBFirstWeekOfYear.VBFirstJan1);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.4.1.4 DateSerial</strong> Gets the date of a year, a month and a day.
    /// </summary>
    /// <remarks>
    /// A month or a day outside its range carries into the next larger unit: day 35 is a month and some days on, day 0 the last day of the month before. A year
    /// from 0 to 99 is a two-digit year: 0 to 29 are 2000 to 2029, and 30 to 99 are 1930 to 1999 (Hijri: 1400 to 1499).
    /// </remarks>
    /// <param name="year">The year, from 100 to 9999.</param>
    /// <param name="month">The month.</param>
    /// <param name="day">The day.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBVariantValue> DateSerial(VBIntegerValue year, VBIntegerValue month, VBIntegerValue day);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.4.1.5 DateValue</strong> Gets the date a string says, without its time.
    /// </summary>
    /// <param name="date">A date, from January 1, 100 to December 31, 9999; it may have a time too.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBVariantValue> DateValue(VBStringValue date);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.4.1.6 Day</strong> Gets the day of the month of a date, from 1 to 31; <c>Null</c> of <c>Null</c>.
    /// </summary>
    /// <param name="date">A value that is let-coercible to a <c>Date</c>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBVariantValue> Day(VBVariantValue date);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.4.1.7 Hour</strong> Gets the hour of the day of a date, from 0 to 23; <c>Null</c> of <c>Null</c>.
    /// </summary>
    /// <param name="time">A value that is let-coercible to a <c>Date</c>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBVariantValue> Hour(VBVariantValue time);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.4.1.8 Minute</strong> Gets the minute of the hour of a date, from 0 to 59; <c>Null</c> of <c>Null</c>.
    /// </summary>
    /// <param name="time">A value that is let-coercible to a <c>Date</c>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBVariantValue> Minute(VBVariantValue time);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.4.1.9 Month</strong> Gets the month of the year of a date, from 1 to 12; <c>Null</c> of <c>Null</c>.
    /// </summary>
    /// <param name="date">A value that is let-coercible to a <c>Date</c>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBVariantValue> Month(VBVariantValue date);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.4.1.10 Second</strong> Gets the second of the minute of a date, from 0 to 59; <c>Null</c> of <c>Null</c>.
    /// </summary>
    /// <param name="time">A value that is let-coercible to a <c>Date</c>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBVariantValue> Second(VBVariantValue time);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.4.1.11 TimeSerial</strong> Gets the time of an hour, a minute and a second.
    /// </summary>
    /// <remarks>
    /// A minute or a second outside its range carries into the next larger unit: 75 minutes is an hour and 15 minutes.
    /// </remarks>
    /// <param name="hour">The hour, from 0 to 23.</param>
    /// <param name="minute">The minute.</param>
    /// <param name="second">The second.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBVariantValue> TimeSerial(VBIntegerValue hour, VBIntegerValue minute, VBIntegerValue second);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.4.1.12 TimeValue</strong> Gets the time a string says, without its date.
    /// </summary>
    /// <param name="time">A time, from 0:00:00 to 23:59:59; it may have a date too.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBVariantValue> TimeValue(VBStringValue time);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.4.1.13 Weekday</strong> Gets the day of the week of a date, counted from <paramref name="firstDayOfWeek"/> as 1; <c>Null</c> of <c>Null</c>.
    /// </summary>
    /// <param name="date">A value that is let-coercible to a <c>Date</c>.</param>
    /// <param name="firstDayOfWeek">The first day of the week. Sunday when unspecified.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBVariantValue> Weekday(VBVariantValue date, VBDayOfWeek firstDayOfWeek = VBDayOfWeek.VBSunday);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.4.1.14 Year</strong> Gets the year of a date, from 100 to 9999; <c>Null</c> of <c>Null</c>.
    /// </summary>
    /// <param name="date">A value that is let-coercible to a <c>Date</c>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBVariantValue> Year(VBVariantValue date);
    #endregion

    #region 6.1.2.4.2 Public Properties
    /// <summary>
    /// <strong>MS-VBAL 6.1.2.4.2.1 Calendar</strong> Gets the calendar the functions of the module work in.
    /// </summary>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    [StdLibMember(Kind = StdLibMemberKind.PropertyGet, ReturnType = typeof(VBCalendar))]
    RuntimeSemanticsEvaluationResult<VBLongValue> Calendar();

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.4.2.1 Calendar</strong> Sets the calendar the functions of the module work in.
    /// </summary>
    /// <param name="value">The calendar.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    [StdLibMember(Kind = StdLibMemberKind.PropertyLet)]
    RuntimeSemanticsEvaluationResult Calendar(VBCalendar value);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.4.2.2 Date</strong> Gets the current date of the system, without its time.
    /// </summary>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    [StdLibMember(Kind = StdLibMemberKind.PropertyGet)]
    RuntimeSemanticsEvaluationResult<VBVariantValue> Date();

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.4.2.2 Date$</strong> Gets the current date of the system as a 10-character string, <c>mm-dd-yyyy</c>, in the current calendar.
    /// </summary>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    [StdLibMember("Date$", Kind = StdLibMemberKind.PropertyGet)]
    RuntimeSemanticsEvaluationResult<VBStringValue> DateStr();

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.4.2.3 Now</strong> Gets the current date and time of the system.
    /// </summary>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    [StdLibMember(Kind = StdLibMemberKind.PropertyGet)]
    RuntimeSemanticsEvaluationResult<VBVariantValue> Now();

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.4.2.4 Time</strong> Gets the current time of the system, without its date.
    /// </summary>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    [StdLibMember(Kind = StdLibMemberKind.PropertyGet)]
    RuntimeSemanticsEvaluationResult<VBVariantValue> Time();

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.4.2.4 Time$</strong> Gets the current time of the system as an 8-character string, <c>hh:mm:ss</c>.
    /// </summary>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    [StdLibMember("Time$", Kind = StdLibMemberKind.PropertyGet)]
    RuntimeSemanticsEvaluationResult<VBStringValue> TimeStr();

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.4.2.4 Time</strong> Sets the time of the system: of the session's clock (<see cref="ISessionClock"/>), never the machine's.
    /// </summary>
    /// <param name="value">A value that is let-coercible to a <c>Date</c>: its time is the time set.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    [StdLibMember(Kind = StdLibMemberKind.PropertyLet)]
    RuntimeSemanticsEvaluationResult Time(VBVariantValue value);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.4.2.5 Timer</strong> Gets the number of seconds elapsed since midnight, with its fraction.
    /// </summary>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    [StdLibMember(Kind = StdLibMemberKind.PropertyGet)]
    RuntimeSemanticsEvaluationResult<VBSingleValue> Timer();
    #endregion
}
