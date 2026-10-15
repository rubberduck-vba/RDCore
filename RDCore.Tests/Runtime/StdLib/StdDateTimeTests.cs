using RDCore.Runtime.Execution;
using RDCore.Runtime.StdLib;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Abstract.StdLib;
using System.Globalization;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// The <c>VBA.DateTime</c> module (<strong>MS-VBAL §6.1.2.4</strong>), through the pipeline that runs it: resolved by name, called with let-coerced arguments,
/// against a session whose clock the test sets.
/// </summary>
[TestClass]
public sealed class StdDateTimeTests
{
    // Wednesday, October 14, 2026, 13:45:30.25, in a zone with no offset so that local time is the time given.
    private static readonly DateTimeOffset Moment = new(2026, 10, 14, 13, 45, 30, 250, TimeSpan.Zero);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Current { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Current;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static (string[] Lines, IRuntimeSession Session) RunWith(FixedClock clock, params string[] body)
    {
        var output = new RuntimeOutputBuffer();
        var (session, outcome) = RuntimeSourceHarness.Run(
            null, [], output, standardLibrary: true, arrange: session => ((SessionClock)session.Clock).Source = clock, ModuleDirectives.None, body);
        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        return ([.. output.Lines.Select(line => line.Trim())], session);
    }

    private static string[] Run(params string[] body) => RunWith(new FixedClock(Moment), body).Lines;

    private static int Fail(params string[] body)
    {
        var (_, outcome) = RuntimeSourceHarness.Run(
            null, [], new RuntimeOutputBuffer(), standardLibrary: true,
            arrange: session => ((SessionClock)session.Clock).Source = new FixedClock(Moment), ModuleDirectives.None, body);
        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind, "an error that says what it is, and not an internal one");
        return outcome.ErrorInfo!.ErrorId;
    }

    private static string Ymd(string date) => $"Debug.Print Year({date}) & \"-\" & Month({date}) & \"-\" & Day({date})";

    private static string Hms(string date) => $"Debug.Print Hour({date}) & \":\" & Minute({date}) & \":\" & Second({date})";

    // ---- DateAdd ----

    [TestMethod]
    public void DateAdd_AMonthAfterTheLastDayOfAMonth_IsTheLastDayOfTheNext_NotAnInvalidDate()
        => CollectionAssert.AreEqual(new[] { "1995-2-28", "1996-2-29" },
            Run(Ymd("DateAdd(\"m\", 1, DateSerial(1995, 1, 31))"), Ymd("DateAdd(\"m\", 1, DateSerial(1996, 1, 31))")));

    [TestMethod]
    [DataRow("yyyy", "2027-10-14")]
    [DataRow("q", "2027-1-14")]
    [DataRow("y", "2026-10-15")]
    [DataRow("d", "2026-10-15")]
    [DataRow("w", "2026-10-15", DisplayName = "w adds a day, not a working day")]
    [DataRow("ww", "2026-10-21")]
    [DataRow("YYYY", "2027-10-14", DisplayName = "the interval is not case sensitive")]
    public void DateAdd_AddsTheInterval(string interval, string expected)
        => CollectionAssert.AreEqual(new[] { expected }, Run(Ymd($"DateAdd(\"{interval}\", 1, DateSerial(2026, 10, 14))")));

    [TestMethod]
    public void DateAdd_AnHourAddedToADateBefore1899_GoesForward()
        // -1.25 is December 29, 1899 at 6:00: the fraction of a negative serial is still the time of its day.
        => CollectionAssert.AreEqual(new[] { "1899-12-29", "7:0:0" }, Run(Ymd("DateAdd(\"h\", 1, CDate(-1.25))"), Hms("DateAdd(\"h\", 1, CDate(-1.25))")));

    [TestMethod]
    public void DateAdd_RoundsTheNumberToTheNearestWholeNumber_HalfToEven()
        => CollectionAssert.AreEqual(new[] { "2026-10-16", "2026-10-18" },
            Run(Ymd("DateAdd(\"d\", 2.5, DateSerial(2026, 10, 14))"), Ymd("DateAdd(\"d\", 3.5, DateSerial(2026, 10, 14))")));

    [TestMethod]
    [DataRow("Debug.Print DateAdd(\"yyyy\", -2000, DateSerial(2026, 1, 1))", DisplayName = "before the year 100")]
    [DataRow("Debug.Print DateAdd(\"yyyy\", 8000, DateSerial(2026, 1, 1))", DisplayName = "after the year 9999")]
    [DataRow("Debug.Print DateAdd(\"x\", 1, DateSerial(2026, 1, 1))", DisplayName = "an interval that is none")]
    public void DateAdd_ADateOutOfRange_OrAnIntervalThatIsNone_IsError5(string statement)
        => Assert.AreEqual(5, Fail(statement));

    [TestMethod]
    public void DateAdd_OfNull_IsTheErrorOfLetCoercingNullToADate()
        => Assert.AreEqual(94, Fail("Debug.Print DateAdd(\"d\", 1, Null)"));

    // ---- DateDiff ----

    [TestMethod]
    public void DateDiff_FromDecember31ToJanuary1_IsAYear()
        => CollectionAssert.AreEqual(new[] { "1" }, Run("Debug.Print DateDiff(\"yyyy\", DateSerial(2025, 12, 31), DateSerial(2026, 1, 1))"));

    [TestMethod]
    public void DateDiff_ThatIsLaterFirst_IsNegative()
        => CollectionAssert.AreEqual(new[] { "-14" }, Run("Debug.Print DateDiff(\"d\", DateSerial(2026, 10, 14), DateSerial(2026, 9, 30))"));

    [TestMethod]
    public void DateDiff_CountsTheBoundariesOfTheInterval_NotTheTimeBetween()
        => CollectionAssert.AreEqual(new[] { "1", "1" }, Run(
            "Debug.Print DateDiff(\"d\", DateSerial(2026, 1, 1) + TimeSerial(23, 59, 0), DateSerial(2026, 1, 2) + TimeSerial(0, 1, 0))",
            "Debug.Print DateDiff(\"h\", TimeSerial(1, 59, 0), TimeSerial(2, 1, 0))"));

    [TestMethod]
    public void DateDiff_ByWeekday_CountsTheDayOfTheWeekOfTheFirstDate_ByCalendarWeek_TheFirstDayOfTheWeek()
    {
        // Saturday, October 10 to Sunday, October 18: one Saturday after the first date, and two Sundays.
        CollectionAssert.AreEqual(new[] { "1", "2", "1" }, Run(
            "Debug.Print DateDiff(\"w\", DateSerial(2026, 10, 10), DateSerial(2026, 10, 18))",
            "Debug.Print DateDiff(\"ww\", DateSerial(2026, 10, 10), DateSerial(2026, 10, 18))",
            "Debug.Print DateDiff(\"ww\", DateSerial(2026, 10, 10), DateSerial(2026, 10, 18), vbMonday)"));
    }

    [TestMethod]
    public void DateDiff_ByCalendarWeek_DoesNotCountTheFirstDate_EvenOnTheFirstDayOfTheWeek()
        => CollectionAssert.AreEqual(new[] { "0", "1" }, Run(
            "Debug.Print DateDiff(\"ww\", DateSerial(2026, 10, 11), DateSerial(2026, 10, 17))",
            "Debug.Print DateDiff(\"ww\", DateSerial(2026, 10, 11), DateSerial(2026, 10, 18))"));

    [TestMethod]
    public void DateDiff_OfSecondsAcrossTheRangeOfDates_IsMoreThanALongHolds()
        => Assert.AreEqual(6, Fail("Debug.Print DateDiff(\"s\", DateSerial(100, 1, 1), DateSerial(9999, 12, 31))"));

    // ---- DatePart ----

    [TestMethod]
    [DataRow("yyyy", "2026")]
    [DataRow("q", "4")]
    [DataRow("m", "10")]
    [DataRow("y", "287")]
    [DataRow("d", "14")]
    [DataRow("w", "4", DisplayName = "w is the day of the week, Sunday first")]
    [DataRow("h", "13")]
    [DataRow("n", "45")]
    [DataRow("s", "30")]
    public void DatePart_IsThePartOfTheDate(string interval, string expected)
        => CollectionAssert.AreEqual(new[] { expected }, Run($"Debug.Print DatePart(\"{interval}\", DateSerial(2026, 10, 14) + TimeSerial(13, 45, 30))"));

    [TestMethod]
    public void DatePart_OfTheWeek_FollowsTheRuleOfTheFirstWeekOfTheYear()
        // Friday, January 1, 2021: in the week of January 1, or the last week of 2020 when the first week has four days of the year.
        => CollectionAssert.AreEqual(new[] { "1", "53" }, Run(
            "Debug.Print DatePart(\"ww\", DateSerial(2021, 1, 1), vbMonday)",
            "Debug.Print DatePart(\"ww\", DateSerial(2021, 1, 1), vbMonday, vbFirstFourDays)"));

    // the module itself, over a session whose clock is the test's: for what a program cannot see without IsNull and TypeName.
    private static StdDateTime Module()
    {
        var clock = new FixedClock(Moment);
        var (session, _) = RuntimeSourceHarness.Run(
            null, [], new RuntimeOutputBuffer(), standardLibrary: true, arrange: session => ((SessionClock)session.Clock).Source = clock, ModuleDirectives.None, "Dim z As Long");
        return new StdDateTime(session);
    }

    private static VBTypedValue? Held(RuntimeSemanticsEvaluationResult<VBVariantValue> result)
    {
        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Verbose);
        return result.Result!.TypedValue;
    }

    [TestMethod]
    public void TheFunctionsThatTheSpecificationDeclaresWithNoType_AreVariants_OfTheTypeTheyDescribe()
    {
        var module = Module();
        var date = new VBVariantValue(new VBDateValue(46309.5));
        var interval = new VBStringValue("d");

        Assert.IsInstanceOfType<VBIntegerValue>(Held(module.Day(date)));
        Assert.IsInstanceOfType<VBIntegerValue>(Held(module.Weekday(date)));
        Assert.IsInstanceOfType<VBIntegerValue>(Held(module.DatePart(interval, date)));
        Assert.IsInstanceOfType<VBLongValue>(Held(module.DateDiff(interval, date, date)));
        Assert.IsInstanceOfType<VBDateValue>(Held(module.DateAdd(interval, new VBDoubleValue(1), date)));
        Assert.IsInstanceOfType<VBDateValue>(Held(module.DateSerial(new VBIntegerValue(2026), new VBIntegerValue(10), new VBIntegerValue(14))));
        Assert.IsInstanceOfType<VBDateValue>(Held(module.TimeSerial(new VBIntegerValue(1), new VBIntegerValue(2), new VBIntegerValue(3))));
        Assert.IsInstanceOfType<VBDateValue>(Held(module.Now()));
    }

    // ---- DateSerial, TimeSerial ----

    [TestMethod]
    public void DateSerial_ATwoDigitYear_IsOfThisCenturyTo29_AndOfTheLastFrom30()
        => CollectionAssert.AreEqual(new[] { "2029", "1930" }, Run("Debug.Print Year(DateSerial(29, 1, 1))", "Debug.Print Year(DateSerial(30, 1, 1))"));

    [TestMethod]
    public void DateSerial_AMonthOrADayOutOfItsRange_CarriesIntoTheNextUnit()
        => CollectionAssert.AreEqual(new[] { "2027-1-1", "2026-9-30", "2026-11-4" }, Run(
            Ymd("DateSerial(2026, 13, 1)"), Ymd("DateSerial(2026, 10, 0)"), Ymd("DateSerial(2026, 10, 35)")));

    [TestMethod]
    public void DateSerial_ADateBeforeTheYear100_IsError5()
        => Assert.AreEqual(5, Fail("Debug.Print DateSerial(100, 1, 0)"));

    [TestMethod]
    public void DateSerial_AnArgumentThatIsNoInteger_IsTheOverflowOfLetCoercingIt()
        => Assert.AreEqual(6, Fail("Debug.Print DateSerial(2026, 1, 40000)"));

    [TestMethod]
    public void TimeSerial_CarriesMinutesIntoHours_AndBeforeMidnightIntoTheDayBefore()
        => CollectionAssert.AreEqual(new[] { "1:15:0", "23:0:0", "1899-12-29" }, Run(
            Hms("TimeSerial(0, 75, 0)"), Hms("TimeSerial(-1, 0, 0)"), Ymd("TimeSerial(-1, 0, 0)")));

    // ---- DateValue, TimeValue ----

    [TestMethod]
    public void DateValue_IsTheDateOfTheString_WithoutItsTime_AndTimeValue_TheTime_WithoutItsDate()
        => CollectionAssert.AreEqual(new[] { "2020-3-4", "0:0:0", "1899-12-30", "10:30:0" }, Run(
            Ymd("DateValue(\"2020-03-04 10:30\")"), Hms("DateValue(\"2020-03-04 10:30\")"),
            Ymd("TimeValue(\"2020-03-04 10:30\")"), Hms("TimeValue(\"2020-03-04 10:30\")")));

    [TestMethod]
    public void DateValue_OfAStringThatIsNoDate_IsATypeMismatch()
        => Assert.AreEqual(13, Fail("Debug.Print DateValue(\"not a date\")"));

    // ---- the parts of a date ----

    [TestMethod]
    [DataRow("Day")]
    [DataRow("Month")]
    [DataRow("Year")]
    [DataRow("Hour")]
    [DataRow("Minute")]
    [DataRow("Second")]
    [DataRow("Weekday")]
    public void APartOfNull_IsNull(string function)
    {
        var module = Module();
        var @null = new VBVariantValue(VBNullValue.Null);
        var result = function switch
        {
            "Day" => module.Day(@null),
            "Month" => module.Month(@null),
            "Year" => module.Year(@null),
            "Hour" => module.Hour(@null),
            "Minute" => module.Minute(@null),
            "Second" => module.Second(@null),
            _ => module.Weekday(@null),
        };

        Assert.IsInstanceOfType<VBNullValue>(Held(result));
    }

    [TestMethod]
    public void APartOfADate_IsOfTheDateRoundedToTheNearestSecond()
        => CollectionAssert.AreEqual(new[] { "1", "0" }, Run("Debug.Print Second(CDate(0.6 / 86400))", "Debug.Print Second(CDate(0.4 / 86400))"));

    [TestMethod]
    public void Weekday_IsCountedFromTheFirstDayOfTheWeek()
        // Wednesday, October 14, 2026.
        => CollectionAssert.AreEqual(new[] { "4", "3", "1" }, Run(
            "Debug.Print Weekday(DateSerial(2026, 10, 14))",
            "Debug.Print Weekday(DateSerial(2026, 10, 14), vbMonday)",
            "Debug.Print Weekday(DateSerial(2026, 10, 14), vbWednesday)"));

    [TestMethod]
    public void AFirstDayOfTheWeekThatIsNone_IsError5()
        => Assert.AreEqual(5, Fail("Debug.Print Weekday(Now, 8)"));

    // ---- Calendar ----

    [TestMethod]
    public void Calendar_IsGregorian_UntilItIsSetToHijri_AndTheFunctionsAreInTheCalendarItIs()
    {
        var newYear = new HijriCalendar().ToDateTime(1448, 1, 1, 0, 0, 0, 0);
        var lines = Run(
            "Debug.Print Calendar",
            "Calendar = vbCalHijri",
            "Debug.Print Calendar",
            "Dim d As Date",
            "d = DateSerial(1448, 1, 1)",
            Ymd("d"),
            "Calendar = vbCalGreg",
            Ymd("d"));

        CollectionAssert.AreEqual(new[] { "0", "1", "1448-1-1", $"{newYear.Year}-{newYear.Month}-{newYear.Day}" }, lines);
    }

    [TestMethod]
    public void Calendar_ThatIsNone_IsError5()
        => Assert.AreEqual(5, Fail("Calendar = 7"));

    [TestMethod]
    public void UnderHijri_ADateBeforeTheCalendarBegins_HasNoYear()
        // serial -600000 is in the year 257, before the Hijri calendar's first day (July 18, 622).
        => Assert.AreEqual(5, Fail("Calendar = vbCalHijri", "Debug.Print Year(CDate(-600000))"));

    // ---- the clock ----

    [TestMethod]
    public void TheProperties_ReadTheClockOfTheSession_ToTheSecond()
        => CollectionAssert.AreEqual(
            new[] { "2026-10-14", "13:45:30", "2026-10-14", "0:0:0", "1899-12-30", "13:45:30", "10-14-2026", "13:45:30", "49530.25" },
            Run(Ymd("Now"), Hms("Now"), Ymd("Date"), Hms("Date"), Ymd("Time"), Hms("Time"), "Debug.Print Date$", "Debug.Print Time$", "Debug.Print Timer"));

    [TestMethod]
    public void DateDollar_IsTheHijriDate_UnderHijri()
    {
        var hijri = new HijriCalendar();
        var expected = string.Create(CultureInfo.InvariantCulture,
            $"{hijri.GetMonth(Moment.DateTime):00}-{hijri.GetDayOfMonth(Moment.DateTime):00}-{hijri.GetYear(Moment.DateTime):0000}");

        CollectionAssert.AreEqual(new[] { expected }, Run("Calendar = vbCalHijri", "Debug.Print Date$"));
    }

    [TestMethod]
    public void Time_ThatIsSet_IsTheTimeOfTheSessionsClock_WhichGoesOnFromIt_AndTheDateIsWhatItWas()
    {
        var clock = new FixedClock(Moment);
        var (lines, session) = RunWith(clock, "Time = \"08:00:00\"", "Debug.Print Time$", "Debug.Print Date$");
        CollectionAssert.AreEqual(new[] { "08:00:00", "10-14-2026" }, lines);

        clock.Current = Moment.AddSeconds(90);
        Assert.AreEqual(new DateTime(2026, 10, 14, 8, 1, 30), session.Clock.Now.AddTicks(-(session.Clock.Now.Ticks % TimeSpan.TicksPerSecond)));
    }

    [TestMethod]
    public void Time_ThatIsNoTime_IsTheErrorOfLetCoercingIt()
        => Assert.AreEqual(13, Fail("Time = \"not a time\""));

    [TestMethod]
    public void AnEnd_SetsTheClockBackToTheMachines_AndTheCalendarToGregorian()
    {
        var clock = new FixedClock(Moment);
        var (_, session) = RunWith(clock, "Time = \"08:00:00\"", "Calendar = vbCalHijri");

        SessionWipe.End(session);

        Assert.AreEqual(Moment.DateTime, session.Clock.Now);
        Assert.AreEqual(VBCalendar.VBCalGreg, session.Clock.Calendar);
    }
}
