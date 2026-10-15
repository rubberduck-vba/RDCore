using RDCore.Runtime.Semantics.Conversion;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Abstract.StdLib;
using RDCore.SDK.Runtime.Shared;
using System.Globalization;
using Intervals = RDCore.SDK.Runtime.Abstract.StdLib.IStdDateTimeModule.Intervals;
using SystemCalendar = System.Globalization.Calendar;

namespace RDCore.Runtime.StdLib;

/// <inheritdoc cref="IStdDateTimeModule"/>
/// <remarks>
/// <para>
/// A <c>Date</c> is the serial number of OLE Automation: days since December 30, 1899, and the time as the fraction of a day, whose sign is the date's even
/// before 1899 (−1.25 is December 29, 1899, 6:00). Each function turns the serial into a point on a line of time, does its arithmetic there, and turns the
/// result back: which is how a date before 1899 adds an hour the right way.
/// </para>
/// <para>
/// The parts of a date are those of the date rounded to the nearest second, as MS-VBA has them: <c>Second(#0:00:00.6#)</c> is 1. The functions of the year,
/// the month, the week and the day are in the calendar of the session (<see cref="ISessionClock.Calendar"/>); hours, minutes and seconds are the same in both.
/// </para>
/// <para>
/// 👉 What the specification leaves unsaid is decided here, and tabulated in RD-VBAL §6.1.2.4: a date out of the range of dates, or an argument that is no
/// value of its enumeration, is error 5; a <c>Null</c> date is let-coerced like any other value, which is error 94, except where the function says it returns
/// <c>Null</c>.
/// </para>
/// </remarks>
/// <param name="session">The session whose clock and calendar the module reads.</param>
public sealed class StdDateTime(IRuntimeSession session) : IStdDateTimeModule
{
    // day 0 of the serial numbers, which is where a time with no date is.
    private static readonly DateTime SerialZero = new(1899, 12, 30);

    private static readonly GregorianCalendar Gregorian = new();
    private static readonly HijriCalendar Hijri = new();

    private SystemCalendar CurrentCalendar => session.Clock.Calendar is VBCalendar.VBCalHijri ? Hijri : Gregorian;

    // the session's clock, to the second: a date of the system has no fraction of a second.
    private DateTime SystemNow
    {
        get
        {
            var now = session.Clock.Now;
            return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));
        }
    }

    #region 6.1.2.4.1 Public Functions

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> DateAdd(VBStringValue interval, VBDoubleValue number, VBVariantValue date)
    {
        if (!TryReadDate(date, out var start, out var error))
        {
            return Fail(error);
        }

        // "If it is not a integer value, it is rounded to the nearest whole number": to even, as the language rounds.
        var rounded = Math.Round(number.Value, MidpointRounding.ToEven);
        if (rounded is < int.MinValue or > int.MaxValue)
        {
            return OutOfRange(nameof(DateAdd));
        }

        var count = (int)rounded;
        var calendar = CurrentCalendar;
        DateTime result;
        try
        {
            result = (interval.Value ?? string.Empty).ToLowerInvariant() switch
            {
                Intervals.Year => calendar.AddYears(start, count),
                Intervals.Quarter => calendar.AddMonths(start, checked(count * 3)),
                Intervals.Month => calendar.AddMonths(start, count),
                // a day, a day of the year and a weekday are all a day: DateAdd adds no working days.
                Intervals.DayOfYear or Intervals.Day or Intervals.Weekday => start.AddDays(count),
                Intervals.Week => start.AddDays(7d * count),
                Intervals.Hour => start.AddHours(count),
                Intervals.Minute => start.AddMinutes(count),
                Intervals.Second => start.AddSeconds(count),
                _ => throw new FormatException(),
            };
        }
        catch (FormatException)
        {
            return InvalidInterval(nameof(DateAdd), interval);
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            // "If the calculated date would precede the year 100 ... an error 5 is raised", and one after the year 9999 is out of the range of dates the same way.
            return OutOfRange(nameof(DateAdd));
        }

        return IsInRange(result) ? Succeed(ToDateValue(result)) : OutOfRange(nameof(DateAdd));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <paramref name="firstWeekOfYear"/> is validated and has no effect: no interval of <c>DateDiff</c> counts weeks of the year.
    /// </remarks>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> DateDiff(
        VBStringValue interval, VBVariantValue date1, VBVariantValue date2,
        VBDayOfWeek firstDayOfWeek = VBDayOfWeek.VBSunday, VBFirstWeekOfYear firstWeekOfYear = VBFirstWeekOfYear.VBFirstJan1)
    {
        if (!TryReadDate(date1, out var from, out var error) || !TryReadDate(date2, out var to, out error))
        {
            return Fail(error);
        }

        if (!TryFirstDayOfWeek(firstDayOfWeek, out var weekStart) || !IsDefined(firstWeekOfYear))
        {
            return InvalidArgument(nameof(DateDiff), "the first day of the week and the first week of the year are values of their enumerations");
        }

        var calendar = CurrentCalendar;
        if (!IsOfCalendar(calendar, from) || !IsOfCalendar(calendar, to))
        {
            return OutOfCalendar(nameof(DateDiff));
        }

        long difference;
        switch ((interval.Value ?? string.Empty).ToLowerInvariant())
        {
            case Intervals.Year:
                difference = calendar.GetYear(to) - calendar.GetYear(from);
                break;
            case Intervals.Quarter:
                difference = Quarters(calendar, to) - Quarters(calendar, from);
                break;
            case Intervals.Month:
                difference = Months(calendar, to) - Months(calendar, from);
                break;
            case Intervals.DayOfYear or Intervals.Day:
                difference = (to.Date - from.Date).Days;
                break;
            case Intervals.Weekday:
                // the days of the week date1 falls on, after date1 up to date2 included: whole weeks, toward zero.
                difference = (to.Date - from.Date).Days / 7;
                break;
            case Intervals.Week:
                // the first days of the week after date1 up to date2 included.
                difference = (StartOfWeek(to, weekStart) - StartOfWeek(from, weekStart)).Days / 7;
                break;
            case Intervals.Hour:
                difference = to.Ticks / TimeSpan.TicksPerHour - from.Ticks / TimeSpan.TicksPerHour;
                break;
            case Intervals.Minute:
                difference = to.Ticks / TimeSpan.TicksPerMinute - from.Ticks / TimeSpan.TicksPerMinute;
                break;
            case Intervals.Second:
                difference = to.Ticks / TimeSpan.TicksPerSecond - from.Ticks / TimeSpan.TicksPerSecond;
                break;
            default:
                return InvalidInterval(nameof(DateDiff), interval);
        }

        // "Returns a Long data value": seconds across the whole range of dates are more than a Long holds.
        return difference is < int.MinValue or > int.MaxValue
            ? Fail(VBRuntimeErrorInfo.For(VBRuntimeErrorId.Overflow, default, $"{nameof(DateDiff)}: the difference does not fit a Long."))
            : Succeed(new VBVariantValue(new VBLongValue((int)difference)));
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> DatePart(
        VBStringValue interval, VBVariantValue baseDate,
        VBDayOfWeek firstDayOfWeek = VBDayOfWeek.VBSunday, VBFirstWeekOfYear firstWeekOfYear = VBFirstWeekOfYear.VBFirstJan1)
    {
        if (!TryReadDate(baseDate, out var date, out var error))
        {
            return Fail(error);
        }

        if (!TryFirstDayOfWeek(firstDayOfWeek, out var weekStart) || !TryWeekRule(firstWeekOfYear, out var rule))
        {
            return InvalidArgument(nameof(DatePart), "the first day of the week and the first week of the year are values of their enumerations");
        }

        var calendar = CurrentCalendar;
        if (!IsOfCalendar(calendar, date))
        {
            return OutOfCalendar(nameof(DatePart));
        }

        int part;
        switch ((interval.Value ?? string.Empty).ToLowerInvariant())
        {
            case Intervals.Year:
                part = calendar.GetYear(date);
                break;
            case Intervals.Quarter:
                part = (calendar.GetMonth(date) - 1) / 3 + 1;
                break;
            case Intervals.Month:
                part = calendar.GetMonth(date);
                break;
            case Intervals.DayOfYear:
                part = calendar.GetDayOfYear(date);
                break;
            case Intervals.Day:
                part = calendar.GetDayOfMonth(date);
                break;
            case Intervals.Weekday:
                part = WeekdayOf(date, weekStart);
                break;
            case Intervals.Week:
                // the week a day in the first days of January falls in can be the last of the year before, for the rules that start the year on a later week.
                part = calendar.GetWeekOfYear(date, rule, weekStart);
                break;
            case Intervals.Hour:
                part = date.Hour;
                break;
            case Intervals.Minute:
                part = date.Minute;
                break;
            case Intervals.Second:
                part = date.Second;
                break;
            default:
                return InvalidInterval(nameof(DatePart), interval);
        }

        return Succeed(Integer(part));
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> DateSerial(VBIntegerValue year, VBIntegerValue month, VBIntegerValue day)
    {
        var calendar = CurrentCalendar;
        int fullYear = year.Value;

        // "values between 0 and 29, inclusive, are interpreted as the years 2000-2029 ... between 30 and 99 ... the years 1930-1999", and for a Hijri year
        // "values between 0 and 99, inclusive, are interpreted as the years 1400-1499".
        if (fullYear is >= 0 and <= 99)
        {
            fullYear += calendar == Hijri ? 1400 : fullYear <= 29 ? 2000 : 1900;
        }

        // a month outside 1 to 12 is years on or back, and a day outside the month is days on or back from its first.
        var months = fullYear * 12L + (month.Value - 1);
        var carriedYear = (int)Math.Floor(months / 12d);
        var carriedMonth = (int)(months - carriedYear * 12L) + 1;

        DateTime result;
        try
        {
            result = calendar.ToDateTime(carriedYear, carriedMonth, 1, 0, 0, 0, 0).AddDays(day.Value - 1);
        }
        catch (ArgumentException)
        {
            return OutOfRange(nameof(DateSerial));
        }

        return IsInRange(result) ? Succeed(ToDateValue(result)) : OutOfRange(nameof(DateSerial));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The string is let-coerced to a <c>Date</c> (<strong>MS-VBAL §5.5.1.2.4</strong>), which is what decides what it can say; a time that cannot be one makes it
    /// no date at all, which is error 13.
    /// </remarks>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> DateValue(VBStringValue date)
        => TryReadDate(new VBVariantValue(date), out var value, out var error) ? Succeed(ToDateValue(value.Date)) : Fail(error);

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> Day(VBVariantValue date)
        => CalendarPart(date, nameof(Day), (calendar, value) => calendar.GetDayOfMonth(value));

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> Hour(VBVariantValue time)
        => Part(time, value => value.Hour);

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> Minute(VBVariantValue time)
        => Part(time, value => value.Minute);

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> Month(VBVariantValue date)
        => CalendarPart(date, nameof(Month), (calendar, value) => calendar.GetMonth(value));

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> Second(VBVariantValue time)
        => Part(time, value => value.Second);

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> TimeSerial(VBIntegerValue hour, VBIntegerValue minute, VBIntegerValue second)
    {
        // a time before midnight of day 0 is a time of the day before, and so on back: the serial of a date before 1899 says it the way OLE Automation does.
        var seconds = hour.Value * 3600L + minute.Value * 60L + second.Value;
        var result = SerialZero.AddSeconds(seconds);
        return IsInRange(result) ? Succeed(ToDateValue(result)) : OutOfRange(nameof(TimeSerial));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The string is let-coerced to a <c>Date</c> (<strong>MS-VBAL §5.5.1.2.4</strong>), and "the date portions of the data value are set to zero".
    /// </remarks>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> TimeValue(VBStringValue time)
        => TryReadDate(new VBVariantValue(time), out var value, out var error) ? Succeed(ToDateValue(SerialZero + value.TimeOfDay)) : Fail(error);

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> Weekday(VBVariantValue date, VBDayOfWeek firstDayOfWeek = VBDayOfWeek.VBSunday)
        => TryFirstDayOfWeek(firstDayOfWeek, out var weekStart)
            ? Part(date, value => WeekdayOf(value, weekStart))
            : InvalidArgument(nameof(Weekday), "the first day of the week is a value of its enumeration");

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> Year(VBVariantValue date)
        => CalendarPart(date, nameof(Year), (calendar, value) => calendar.GetYear(value));

    #endregion

    #region 6.1.2.4.2 Public Properties

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBLongValue> Calendar()
        => RuntimeSemanticsEvaluationResult<VBLongValue>.Success(new VBLongValue((int)session.Clock.Calendar));

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult Calendar(VBCalendar value)
    {
        if (!Enum.IsDefined(value))
        {
            return RuntimeSemanticsEvaluationResult.Error(VBRuntimeErrorInfo.For(
                VBRuntimeErrorId.InvalidProcedureCallOrArgument, default, $"{nameof(Calendar)}: {(int)value} is not a calendar."));
        }

        session.Clock.Calendar = value;
        return RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void);
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> Date()
        => Succeed(ToDateValue(SystemNow.Date));

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBStringValue> DateStr()
    {
        var now = SystemNow;
        var calendar = CurrentCalendar;
        return RuntimeSemanticsEvaluationResult<VBStringValue>.Success(new VBStringValue(
            string.Create(CultureInfo.InvariantCulture, $"{calendar.GetMonth(now):00}-{calendar.GetDayOfMonth(now):00}-{calendar.GetYear(now):0000}")));
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> Now()
        => Succeed(ToDateValue(SystemNow));

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> Time()
        => Succeed(ToDateValue(SerialZero + SystemNow.TimeOfDay));

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBStringValue> TimeStr()
        => RuntimeSemanticsEvaluationResult<VBStringValue>.Success(new VBStringValue(SystemNow.ToString("HH:mm:ss", CultureInfo.InvariantCulture)));

    /// <inheritdoc/>
    /// <remarks>
    /// "The value assigned to the Time property MUST be Let-coercible to a Date data value": one that is not is the error of that coercion.
    /// </remarks>
    public RuntimeSemanticsEvaluationResult Time(VBVariantValue value)
    {
        if (!TryReadDate(value, out var time, out var error))
        {
            return RuntimeSemanticsEvaluationResult.Error(error);
        }

        session.Clock.SetTime(time.TimeOfDay);
        return RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The fraction of a second is the clock's own: to the tick.
    /// </remarks>
    public RuntimeSemanticsEvaluationResult<VBSingleValue> Timer()
        => RuntimeSemanticsEvaluationResult<VBSingleValue>.Success(new VBSingleValue((float)session.Clock.Now.TimeOfDay.TotalSeconds));

    #endregion

    // MS-VBAL 6.1.2.4.1.6-10, 13-14: "Date is Let-coerced to Date and an Integer ... is returned. If Date is Null, Null is returned."
    private static RuntimeSemanticsEvaluationResult<VBVariantValue> Part(VBVariantValue argument, Func<DateTime, int> part)
    {
        if (Unwrapped(argument) is VBNullValue)
        {
            return Succeed(new VBVariantValue(VBNullValue.Null));
        }

        return TryReadDate(argument, out var date, out var error) ? Succeed(Integer(part(date))) : Fail(error);
    }

    // a part of the date in the current calendar, which a date before the calendar's first day has none of.
    private RuntimeSemanticsEvaluationResult<VBVariantValue> CalendarPart(VBVariantValue argument, string function, Func<SystemCalendar, DateTime, int> part)
    {
        if (Unwrapped(argument) is VBNullValue)
        {
            return Succeed(new VBVariantValue(VBNullValue.Null));
        }

        if (!TryReadDate(argument, out var date, out var error))
        {
            return Fail(error);
        }

        var calendar = CurrentCalendar;
        return IsOfCalendar(calendar, date) ? Succeed(Integer(part(calendar, date))) : OutOfCalendar(function);
    }

    // the Hijri calendar begins in 622: a date before it has no Hijri year, nor month, nor day.
    private static bool IsOfCalendar(SystemCalendar calendar, DateTime date)
        => date >= calendar.MinSupportedDateTime && date <= calendar.MaxSupportedDateTime;

    private static RuntimeSemanticsEvaluationResult<VBVariantValue> OutOfCalendar(string function)
        => InvalidArgument(function, "the date is one of the current calendar");

    // the argument let-coerced to a Date, as a point on the line of time, to the nearest second.
    private static bool TryReadDate(VBVariantValue argument, out DateTime date, out VBRuntimeErrorInfo error)
    {
        date = default;
        error = default!;

        var converted = ValueConversions.ToDate(Unwrapped(argument));
        if (!converted.IsSuccess || converted.Value is not VBDateValue value)
        {
            error = VBRuntimeErrorInfo.For(converted.Error ?? VBRuntimeErrorId.TypeMismatch, default, "The argument is not let-coercible to a Date.");
            return false;
        }

        var exact = System.DateTime.FromOADate(value.SerialValue);
        var rounded = (exact.Ticks + TimeSpan.TicksPerSecond / 2) / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond;
        date = new DateTime(Math.Min(rounded, System.DateTime.MaxValue.Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond));
        return true;
    }

    private static VBTypedValue Unwrapped(VBVariantValue argument)
    {
        VBTypedValue value = argument;
        while (value is VBVariantValue { TypedValue: { } wrapped })
        {
            value = wrapped;
        }

        return value;
    }

    // MS-VBAL 6.1.1.7: Sunday is 1 and Saturday 7, and 0 is the first day of the week of the system.
    private static bool TryFirstDayOfWeek(VBDayOfWeek firstDayOfWeek, out DayOfWeek weekStart)
    {
        weekStart = firstDayOfWeek is VBDayOfWeek.VBUseSystemDayOfWeek
            ? CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek
            : (DayOfWeek)((int)firstDayOfWeek - 1);
        return Enum.IsDefined(firstDayOfWeek);
    }

    // MS-VBAL 6.1.1.9: the week of January 1, the first week of at least four days, the first full week, or the system's.
    private static bool TryWeekRule(VBFirstWeekOfYear firstWeekOfYear, out CalendarWeekRule rule)
    {
        rule = firstWeekOfYear switch
        {
            VBFirstWeekOfYear.VBFirstFourDays => CalendarWeekRule.FirstFourDayWeek,
            VBFirstWeekOfYear.VBFirstFullWeek => CalendarWeekRule.FirstFullWeek,
            VBFirstWeekOfYear.VBUseSystem => CultureInfo.CurrentCulture.DateTimeFormat.CalendarWeekRule,
            _ => CalendarWeekRule.FirstDay,
        };
        return IsDefined(firstWeekOfYear);
    }

    private static bool IsDefined(VBFirstWeekOfYear firstWeekOfYear) => Enum.IsDefined(firstWeekOfYear);

    // the day of the week, counted from the first day of the week as 1.
    private static int WeekdayOf(DateTime date, DayOfWeek weekStart)
        => ((int)date.DayOfWeek - (int)weekStart + 7) % 7 + 1;

    private static DateTime StartOfWeek(DateTime date, DayOfWeek weekStart)
        => date.Date.AddDays(-(((int)date.DayOfWeek - (int)weekStart + 7) % 7));

    private static long Quarters(SystemCalendar calendar, DateTime date) => calendar.GetYear(date) * 4L + (calendar.GetMonth(date) - 1) / 3;

    private static long Months(SystemCalendar calendar, DateTime date) => calendar.GetYear(date) * 12L + calendar.GetMonth(date);

    // MS-VBAL 2.1: a Date is from January 1, 100 to December 31, 9999.
    private static bool IsInRange(DateTime date) => date.Year is >= 100 and <= 9999;

    private static VBVariantValue ToDateValue(DateTime date) => new(new VBDateValue(date.ToOADate()));

    private static VBVariantValue Integer(int value) => new(new VBIntegerValue((short)value));

    private static RuntimeSemanticsEvaluationResult<VBVariantValue> Succeed(VBVariantValue value)
        => RuntimeSemanticsEvaluationResult<VBVariantValue>.Success(value);

    private static RuntimeSemanticsEvaluationResult<VBVariantValue> Fail(VBRuntimeErrorInfo error)
        => RuntimeSemanticsEvaluationResult<VBVariantValue>.Error(error);

    private static RuntimeSemanticsEvaluationResult<VBVariantValue> InvalidArgument(string function, string rule)
        => Fail(VBRuntimeErrorInfo.For(VBRuntimeErrorId.InvalidProcedureCallOrArgument, default, $"{function}: {rule}."));

    private static RuntimeSemanticsEvaluationResult<VBVariantValue> InvalidInterval(string function, VBStringValue interval)
        => InvalidArgument(function, $"\"{interval.Value}\" is an interval: yyyy, q, m, y, d, w, ww, h, n or s");

    private static RuntimeSemanticsEvaluationResult<VBVariantValue> OutOfRange(string function)
        => InvalidArgument(function, "the date is from January 1, 100 to December 31, 9999");
}
