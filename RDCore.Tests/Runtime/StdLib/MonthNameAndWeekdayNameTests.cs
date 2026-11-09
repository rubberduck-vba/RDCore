using RDCore.Runtime.Execution;
using RDCore.Runtime.StdLib;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Abstract.StdLib;
using static RDCore.Tests.Runtime.StdLib.StdStringsTestHarness;
using RDCore.Runtime.LanguageCore.Errors;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// <strong>MS-VBAL §6.1.2.11.1.28 MonthName</strong> and <strong>§6.1.2.11.1.43 WeekdayName</strong>: the names of a month and of a
/// day of the week, in the regional settings of the session's environment.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 6.1.2.11.1.28 MonthName")]
[TestCategory("MS-VBAL 6.1.2.11.1.43 WeekdayName")]
public sealed class MonthNameAndWeekdayNameTests
{
    private const int EnUS = 1033;
    private static readonly VBBooleanValue Full = new(false);
    private static readonly VBBooleanValue Short = new(true);

    [TestMethod]
    [DataRow(1, false, "January")]
    [DataRow(12, false, "December")]
    [DataRow(3, true, "Mar")]
    [DataRow(9, true, "Sep")]
    public void MonthName_EnUS(int month, bool abbreviate, string expected)
        => Assert.AreEqual(expected, Module(EnUS).MonthName(L(month), abbreviate ? Short : Full).Result!.Value);

    [TestMethod]
    public void MonthName_IsInTheEnvironmentsRegionalSettings()
    {
        Assert.AreEqual("mars", Module(1036).MonthName(L(3), Full).Result!.Value);
        Assert.AreEqual("März", Module(1031).MonthName(L(3), Full).Result!.Value);
    }

    [TestMethod]
    public void MonthName_UnderTheHijriCalendar_IsAHijriMonth_WithNoAbbreviation()
    {
        var session = RuntimeSessionComposer.Compose(new RuntimeEnvironmentProfile(Is64Bit: true, EnUS, 1252, SupportsOptionCompareDatabase: false), new NoSymbols());
        session.Clock.Calendar = VBCalendar.VBCalHijri;
        var module = new StdStrings(session);

        var ramadan = module.MonthName(L(9), Full).Result!.Value;
        Assert.AreEqual(TextOf(module.Format(V(new VBDateValue(new DateTime(2024, 3, 15).ToOADate())), V("mmmm"))), ramadan);
        Assert.AreEqual(ramadan, module.MonthName(L(9), Short).Result!.Value);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(13)]
    [DataRow(-1)]
    public void MonthName_OfNoMonth_IsError5(int month)
        => Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Module().MonthName(L(month), Full)));

    [TestMethod]
    [DataRow(1, VBDayOfWeek.VBSunday, false, "Sunday")]
    [DataRow(7, VBDayOfWeek.VBSunday, false, "Saturday")]
    [DataRow(1, VBDayOfWeek.VBMonday, false, "Monday")]
    [DataRow(7, VBDayOfWeek.VBMonday, true, "Sun")]
    [DataRow(3, VBDayOfWeek.VBFriday, true, "Sun")]
    [DataRow(1, VBDayOfWeek.VBUseSystemDayOfWeek, false, "Sunday")]
    public void WeekdayName_EnUS(int weekday, VBDayOfWeek firstDayOfWeek, bool abbreviate, string expected)
        => Assert.AreEqual(expected, Module(EnUS).WeekdayName(L(weekday), abbreviate ? Short : Full, firstDayOfWeek).Result!.Value);

    [TestMethod]
    public void WeekdayName_ByDefault_CountsFromTheRegionalSettingsFirstDay()
    {
        Assert.AreEqual("lundi", Module(1036).WeekdayName(L(1), Full).Result!.Value, "fr-FR's week begins on Monday");
        Assert.AreEqual("Sunday", Module(EnUS).WeekdayName(L(1), Full).Result!.Value);
    }

    [TestMethod]
    public void WeekdayName_Errors()
    {
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Module().WeekdayName(L(0), Full)));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Module().WeekdayName(L(8), Full)));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Module().WeekdayName(L(1), Full, (VBDayOfWeek)8)));
    }

    [TestMethod]
    public void FromSource()
    {
        CollectionAssert.AreEqual(new[] { "March", "Mar", "Sunday", "Mon", "Tuesday" }, Run(
            "Debug.Print MonthName(3)",
            "Debug.Print MonthName(3, True)",
            "Debug.Print WeekdayName(1)",
            "Debug.Print WeekdayName(1, True, vbMonday)",
            "Debug.Print WeekdayName(Weekday(#3/5/2024#))"));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, RunToError("Debug.Print MonthName(13)"));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidUseOfNull, RunToError("Debug.Print MonthName(Null)"));
    }

    private sealed class NoSymbols : ISymbolProvider
    {
        public IEnumerable<Symbol> ProvideSymbols() => [];
    }
}
