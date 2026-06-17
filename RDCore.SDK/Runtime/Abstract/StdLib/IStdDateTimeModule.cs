using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.SDK.Runtime.Abstract.StdLib;

/// <summary>
/// <strong>MS-VBAL 6.1.2.4 DateTime Module</strong>
/// </summary>
/// <remarks>
/// Formalizes the public interface of the standard library <c>VBA.DateTime</c> module.<br/>
/// </remarks>
public interface IStdDateTimeModule
{
    /// <summary>
    /// Formalizes the valid <see cref="VBStringValue"/> <c>interval</c> parameter values.
    /// </summary>
    /// <remarks>
    /// 💥 Any other value raises run-time error 5 <see cref="VBRuntimeErrorId.InvalidProcedureCallOrArgument"/>.
    /// </remarks>
    internal static class StdDateIntervals
    {
        public const string Year = "yyyy";
        public const string Quarter = "q";
        public const string Month = "m";
        public const string DayOfYear = "y";
        public const string Day = "d";
        public const string Weekday = "w";
        public const string Week = "ww";
        public const string Hour = "h";
        public const string Minute = "n";
        public const string Second = "s";
    }

    #region 6.1.2.4.1 StdDateTime: Public Functions
    /// <summary>
    /// <strong>MS-VBAL 6.1.2.4.1.1 DateAdd</strong>
    /// </summary>
    /// <remarks>
    /// Returns the result of adding or subtracting a specified time interval from a base date.<br/>
    /// The valid <c>interval</c> parameter values are defined in <see cref="StdDateIntervals"/>.
    /// </remarks>
    /// <param name="interval">A <em>string data value</em> that specifies the interval of time to add.</param>
    /// <param name="date">A <em>date data value</em> to which the interval is added.</param>
    /// <param name="number">The number of intervals to add. Can be positive (future) or negative (past). Rounded to the nearest whole number if not an <em>integral numeric value</em>.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdDateTime__DateAdd(VBStringValue interval, VBDoubleValue number, VBVariantValue date);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.4.1.2 DateDiff</strong>
    /// </summary>
    /// <remarks>
    /// Returns the result of adding or subtracting a specified time interval from a base date.<br/>
    /// </remarks>
    /// <param name="interval">A <em>string data value</em> that specifies the interval of time to use to calculate the difference between <c>date1</c> and <c>date2</c>.</param>
    /// <param name="date1">The first date to use in the calculation.</param>
    /// <param name="date2">The second date to use in the calculation.</param>
    /// <param name="firstDayOfWeek">A constant that specifies the <em>first day of the week</em>. This parameter is optional (default: <see cref="VBDayOfWeek.VBSunday"/>).</param>
    /// <param name="firstWeekofYear">A constant that specifies the <em>first week of the year</em>. This parameter is optional (default: <see cref="VBFirstWeekOfYear.VBFirstJan1"/>).</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdDateTime__DateDiff(VBStringValue interval, VBVariantValue date1, VBVariantValue date2, VBDayOfWeek firstDayOfWeek = VBDayOfWeek.VBSunday, VBFirstWeekOfYear firstWeekofYear = VBFirstWeekOfYear.VBFirstJan1);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.4.1.3 DatePart</strong>
    /// </summary>
    /// <remarks>
    /// Returns the result of adding or subtracting a specified time interval from a base date.<br/>
    /// </remarks>
    /// <param name="interval">A <em>string data value</em> that specifies the interval of time to use to calculate the difference between <c>date1</c> and <c>date2</c>.</param>
    /// <param name="date1">The first date to use in the calculation.</param>
    /// <param name="date2">The second date to use in the calculation.</param>
    /// <param name="firstDayOfWeek">A constant that specifies the <em>first day of the week</em>. This parameter is optional (default: <see cref="VBDayOfWeek.VBSunday"/>).</param>
    /// <param name="firstWeekofYear">A constant that specifies the <em>first week of the year</em>. This parameter is optional (default: <see cref="VBFirstWeekOfYear.VBFirstJan1"/>).</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdDateTime__DatePart(VBStringValue interval, VBVariantValue date1, VBVariantValue date2, VBDayOfWeek firstDayOfWeek = VBDayOfWeek.VBSunday, VBFirstWeekOfYear firstWeekofYear = VBFirstWeekOfYear.VBFirstJan1);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.4.1.4 DateSerial</strong>
    /// </summary>
    /// <remarks>
    /// Returns a <see cref="VBDateValue"/> from a specified <c>year</c>, <c>month</c>, and <c>day</c>.<br/>
    /// </remarks>
    /// <param name="year">A <see cref="VBIntegerValue"/> representing the year of the date value.</param>
    /// <param name="month">A <see cref="VBIntegerValue"/> representing the month of the date value.</param>
    /// <param name="day">A <see cref="VBIntegerValue"/> representing the day of the date value.</param>
    RuntimeSemanticsEvaluationResult StdDateTime__DateSerial(VBIntegerValue year, VBIntegerValue month, VBIntegerValue day);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.4.1.5 DateValue</strong>
    /// </summary>
    /// <remarks>
    /// Returns a <see cref="VBDateValue"/> from a specified <see cref="VBStringValue"/>.<br/>
    /// </remarks>
    /// <param name="date">A value that is <em>let-coercible</em> to a <see cref="VBDateValue"/>, holding a date value to be represented.</param>
    RuntimeSemanticsEvaluationResult StdDateTime__DateValue(VBVariantValue date);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.4.1.6 Day</strong>
    /// </summary>
    /// <remarks>
    /// Returns a <see cref="VBVariantValue"/>/<see cref="VBIntegerValue"/> representing the <strong>day of the month</strong>.<br/>
    /// </remarks>
    /// <param name="date">A value that is <em>let-coercible</em> to a <see cref="VBDateValue"/>, holding a date value to be represented.</param>
    RuntimeSemanticsEvaluationResult StdDateTime__Day(VBVariantValue date);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.4.1.7 Hour</strong>
    /// </summary>
    /// <remarks>
    /// Returns a <see cref="VBVariantValue"/>/<see cref="VBIntegerValue"/> between 0 and 23 representing the <strong>hour of the day</strong>.<br/>
    /// </remarks>
    /// <param name="time">A value that is <em>let-coercible</em> to a <see cref="VBDateValue"/>, holding a date value to be represented.</param>
    RuntimeSemanticsEvaluationResult StdDateTime__Hour(VBVariantValue time);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.4.1.8 Minute</strong>
    /// </summary>
    /// <remarks>
    /// Returns a <see cref="VBVariantValue"/>/<see cref="VBIntegerValue"/> between 0 and 59 representing the <strong>minute of the hour</strong>.<br/>
    /// </remarks>
    /// <param name="time">A value that is <em>let-coercible</em> to a <see cref="VBDateValue"/>, holding a date value to be represented.</param>
    RuntimeSemanticsEvaluationResult StdDateTime__Minute(VBVariantValue time);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.4.1.9 Month</strong>
    /// </summary>
    /// <remarks>
    /// Returns a <see cref="VBVariantValue"/>/<see cref="VBIntegerValue"/> between 1 and 12 representing the <strong>month of the year</strong>.<br/>
    /// </remarks>
    /// <param name="date">A value that is <em>let-coercible</em> to a <see cref="VBDateValue"/>, holding a date value to be represented.</param>
    RuntimeSemanticsEvaluationResult StdDateTime__Month(VBVariantValue date);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.4.1.10 Second</strong>
    /// </summary>
    /// <remarks>
    /// Returns a <see cref="VBVariantValue"/>/<see cref="VBIntegerValue"/> between 0 and 59 representing the <strong>second of the minute</strong>.<br/>
    /// </remarks>
    /// <param name="time">A value that is <em>let-coercible</em> to a <see cref="VBDateValue"/>, holding a date value to be represented.</param>
    RuntimeSemanticsEvaluationResult StdDateTime__Second(VBVariantValue time);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.4.1.11 TimeSerial</strong>
    /// </summary>
    /// <remarks>
    /// Returns a <see cref="VBVariantValue"/> holding a <c>date</c> containing the <em>time</em> for a specific <c>hour</c>, <c>minute</c>, and <c>second</c>.<br/>
    /// </remarks>
    /// <param name="hour">An integer between 0 and 23 representing the hour of the day.</param>
    /// <param name="minute">An integer between 0 and 59 representing the minute of the hour.</param>
    /// <param name="second">An integer between 0 and 59 representing the second of the minute.</param>
    RuntimeSemanticsEvaluationResult StdDateTime__TimeSerial(VBIntegerValue hour, VBIntegerValue minute, VBIntegerValue second);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.4.1.12 TimeValue</strong>
    /// </summary>
    /// <remarks>
    /// Returns a <see cref="VBVariantValue"/> (<see cref="VBDateValue"/>) truncated of its <em>date</em> portion.<br/>
    /// </remarks>
    /// <param name="time">A <em>time value</em> to be represented.</param>
    RuntimeSemanticsEvaluationResult StdDateTime__TimeValue(VBStringValue time);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.4.1.13 WeekDay</strong>
    /// </summary>
    /// <remarks>
    /// Returns an <see cref="VBVariantValue"/> (<see cref="VBIntegerValue"/>) representing the <em>day of the week</em> of a specified <see cref="VBDateValue"/>.<br/>
    /// </remarks>
    /// <param name="date">A value that is <em>let-coercible</em>to a <see cref="VBDateValue"/>.</param>
    /// <param name="firstDayOfWeek">A constant that specifies the <em>first day of the week</em>. This parameter is optional (default: <see cref="VBDayOfWeek.VBSunday"/>).</param>
    RuntimeSemanticsEvaluationResult StdDateTime__WeekDay(VBVariantValue date, VBDayOfWeek firstDayOfWeek = VBDayOfWeek.VBSunday);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.4.1.14 Year</strong>
    /// </summary>
    /// <remarks>
    /// Returns a <see cref="VBVariantValue"/>/<see cref="VBIntegerValue"/> representing the <strong>year</strong>.<br/>
    /// </remarks>
    /// <param name="date">A value that is <em>let-coercible</em> to a <see cref="VBDateValue"/>, holding a date value to be represented.</param>
    RuntimeSemanticsEvaluationResult StdDateTime__Year(VBVariantValue date);
    #endregion

    #region 6.1.2.4.2 StdDateTime: Properties
    /// <summary>
    /// <strong>MS-VBAL 6.1.2.4.2.1 Calendar</strong>
    /// </summary>
    /// <remarks>
    /// Gets or sets the <see cref="VBCalendar"/> to use for subsequent calls to <c>VBA.DateTime</c> module functions.
    /// </remarks>
    RuntimeSemanticsEvaluationResult StdDateTime__getCalendar();

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.4.2.1 Calendar</strong>
    /// </summary>
    /// <remarks>
    /// Gets or sets the <see cref="VBCalendar"/> to use for subsequent calls to <c>VBA.DateTime</c> module functions.
    /// </remarks>
    RuntimeSemanticsEvaluationResult StdDateTime__setCalendar(VBCalendar value);

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.4.2.2 Date</strong>
    /// </summary>
    /// <remarks>
    /// Gets a <see cref="VBVariantValue"/> (<see cref="VBDateValue"/>) holding the current system date.
    /// </remarks>
    RuntimeSemanticsEvaluationResult StdDateTime__getDate();
    /// <summary>
    /// <strong>MS-VBAL 6.1.2.4.2.2 Date$</strong>
    /// </summary>
    /// <remarks>
    /// Gets a <see cref="VBStringValue"/> holding a string representation of the current system date.
    /// </remarks>
    RuntimeSemanticsEvaluationResult StdDateTime__getDateStr();

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.4.2.3 Now</strong>
    /// </summary>
    /// <remarks>
    /// Gets a <see cref="VBDateValue"/> holding the current system date and time.
    /// </remarks>
    RuntimeSemanticsEvaluationResult StdDateTime__getNow();

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.4.2.4 Time</strong>
    /// </summary>
    /// <remarks>
    /// Gets a <see cref="VBVariantValue"/> (<see cref="VBDateValue"/>) holding the current system time.
    /// </remarks>
    RuntimeSemanticsEvaluationResult StdDateTime__getTime();
    /// <summary>
    /// <strong>MS-VBAL 6.1.2.4.2.4 Time$</strong>
    /// </summary>
    /// <remarks>
    /// Gets a <see cref="VBStringValue"/> holding a string representation of the current system time.
    /// </remarks>
    RuntimeSemanticsEvaluationResult StdDateTime__getTimeStr();

    /// <summary>
    /// <strong>MS-VBAL 6.1.2.4.2.5 Timer</strong>
    /// </summary>
    /// <remarks>
    /// Gets a <see cref="VBSingleValue"/> representing the <strong>number of seconds</strong> elapsed since midnight.<br/>
    /// 👉 The <em>sub-second</em> resolution is <strong>implementation-dependant</strong>; MS-VBAL resolution measures ticks (15ms).
    /// </remarks>
    RuntimeSemanticsEvaluationResult StdDateTime__getTimer();
    #endregion
}
