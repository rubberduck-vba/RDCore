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

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// <strong>MS-VBAL §6.1.2.11.1.8 Format</strong>: "a String containing an expression formatted according to instructions contained in
/// a format expression", named or user-defined, in the regional settings of the session's environment and its calendar.
/// </summary>
/// <remarks>
/// The examples of the Office VBA reference are recorded for en-US (1033), which is what they assume.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 6.1.2.11.1.8 Format")]
[TestCategory("MS-VBAL 6.1.2.11.1.9 Format$")]
public sealed class FormatTests
{
    private const int EnUS = 1033;

    private static VBVariantValue D(int year, int month, int day, int hour = 0, int minute = 0, int second = 0)
        => V(new VBDateValue(new DateTime(year, month, day, hour, minute, second).ToOADate()));

    private static VBVariantValue T(int hour, int minute, int second) => D(1899, 12, 30, hour, minute, second);

    private static string Formatted(VBVariantValue expression, string format, int lcid = EnUS)
        => Spaced(Module(lcid).Format(expression, V(format)));

    // ICU's en-US time patterns put a narrow no-break space before AM and PM, where Windows' put a space.
    private static string Spaced<TValue>(RDCore.SDK.Runtime.Shared.RuntimeSemanticsEvaluationResult<TValue> result)
        where TValue : RDCore.SDK.Model.Values.Abstract.VBTypedValue
        => TextOf(result).Replace('\u202F', ' ');

    [TestMethod]
    [DataRow(5459.4, "##,##0.00", "5,459.40")]
    [DataRow(334.9, "###0.00", "334.90")]
    [DataRow(5d, "0.00%", "500.00%")]
    [DataRow(5551234567d, "(000) 000-0000", "(555) 123-4567")]
    [DataRow(1234567.891, "#,##0.00", "1,234,567.89")]
    [DataRow(1234567d, "#,#", "1,234,567")]
    [DataRow(100000000d, "##0,,", "100")]
    [DataRow(12345d, "0.00E+00", "1.23E+04")]
    [DataRow(0.000123, "0.00E-00", "1.23E-04")]
    [DataRow(12345d, "0.00e+00", "1.23e+04")]
    [DataRow(99.96, "0.0E+00", "1.0E+02")]
    [DataRow(5d, "#.##", "5.")]
    [DataRow(0.5, "#.##", ".5")]
    [DataRow(0.5, "0.##", "0.5")]
    [DataRow(0.5, ".00", ".50")]
    [DataRow(12.5, ".00", "12.50")]
    [DataRow(7d, "000", "007")]
    [DataRow(-5d, "$0", "-$5")]
    [DataRow(5d, "0 \\h\\o\\u\\r\\s", "5 hours")]
    [DataRow(5d, "0 \"items\"", "5 items")]
    public void UserDefinedNumberFormats(double number, string format, string expected)
        => Assert.AreEqual(expected, Formatted(V(number), format));

    [TestMethod]
    [DataRow(2.5, "0", "3")]
    [DataRow(-2.5, "0", "-3")]
    [DataRow(0.125, "0.00", "0.13")]
    [DataRow(1.005, "0.00", "1.01")]
    [DataRow(0.6, "#", "1")]
    [DataRow(9.995, "0.00", "10.00")]
    public void ANumber_RoundsHalfAwayFromZero_OnItsFifteenSignificantDigits(double number, string format, string expected)
        => Assert.AreEqual(expected, Formatted(V(number), format));

    [TestMethod]
    public void AFixedPointOrWholeNumber_KeepsEveryDigit()
    {
        Assert.AreEqual("123,456,789,012,345,678", Formatted(V(new VBDecimalValue(123456789012345678m)), "#,##0"));
        Assert.AreEqual("9223372036854775807", Formatted(V(new VBLongLongValue(long.MaxValue)), "0"));
        Assert.AreEqual("-9223372036854775808", Formatted(V(new VBLongLongValue(long.MinValue)), "0"));
        Assert.AreEqual("922,337,203,685,477.5807", Formatted(V(new VBCurrencyValue(922337203685477.5807m)), "#,##0.0000"));
        Assert.AreEqual("0.10000", Formatted(V(new VBSingleValue(0.1f)), "0.00000"), "a Single is its 7 significant digits");
    }

    [TestMethod]
    public void TheSection_IsChosenByTheSignBeforeRounding()
    {
        Assert.AreEqual("($1,235)", Formatted(V(-1234.5), "$#,##0;($#,##0)"));
        Assert.AreEqual("$1,235", Formatted(V(1234.5), "$#,##0;($#,##0)"));
        Assert.AreEqual("Zero", Formatted(V(0), "$#,##0;;\\Z\\e\\r\\o"));
        Assert.AreEqual("-$5", Formatted(V(-5), "$#,##0;;\\Z\\e\\r\\o"), "an empty section is the first one's");
        Assert.AreEqual("-0.00", Formatted(V(-0.001), "0.00"));
        Assert.AreEqual("(0.00)", Formatted(V(-0.001), "0.00;(0.00)"));
        Assert.AreEqual("0.00", Formatted(V(0.001), "0.00;(0.00);\"zero\""));
    }

    [TestMethod]
    public void ANull_IsWrittenByItsSection_OrIsNull()
    {
        Assert.AreEqual("nil", TextOf(Module().Format(VNull, V("0;-0;0;\"nil\""))));
        Assert.AreEqual("nil", TextOf(Module().Format(VNull, V("@;\"nil\""))));
        Assert.IsTrue(IsNull(Module().Format(VNull, V("0"))));
        Assert.IsTrue(IsNull(Module().Format(VNull, V("Fixed"))));
        Assert.IsTrue(IsNull(Module().Format(VNull)));
        Assert.IsTrue(IsNull(Module().Format(VNull, V("w"), (VBDayOfWeek)9)), "Null comes before the values' checks");
    }

    [TestMethod]
    public void WithoutAFormat_TheValueIsWrittenAsALetCoercionWritesIt()
    {
        Assert.AreEqual("23", TextOf(Module(EnUS).Format(V(23))));
        Assert.AreEqual("1.5", TextOf(Module(EnUS).Format(V(1.5))));
        Assert.AreEqual("1,5", TextOf(Module(1036, regional: German).Format(V(1.5))));
        Assert.AreEqual("True", TextOf(Module(EnUS).Format(V(true))));
        Assert.AreEqual(string.Empty, TextOf(Module(EnUS).Format(VEmpty)));
        Assert.AreEqual("23", TextOf(Module(EnUS).Format(V(23), V(string.Empty))), "a zero-length format is none");
    }

    [TestMethod]
    public void AString_IsANumberOrADateIfItReadsAsOne_AndElseItself()
    {
        Assert.AreEqual("12.00", Formatted(V("12"), "0.00"));
        Assert.AreEqual("1,234.50", Formatted(V("1234.5"), "#,##0.00"));
        Assert.AreEqual("2020", Formatted(V("1/2/2020"), "yyyy"));
        Assert.AreEqual("abc", Formatted(V("abc"), "0.00"));
        Assert.AreEqual("abc", Formatted(V("abc"), "Short Date"));
        Assert.AreEqual("abc", Formatted(V("abc"), "Fixed"));
    }

    [TestMethod]
    public void ADateOrABoolean_IsTheNumberItIs()
    {
        Assert.AreEqual("43831.00", Formatted(D(2020, 1, 1), "0.00"));
        Assert.AreEqual("-1", Formatted(V(true), "0"));
        Assert.AreEqual("2023-03-15", Formatted(V(45000), "yyyy-mm-dd"));
        Assert.AreEqual("0.00", Formatted(VEmpty, "0.00"));
    }

    [TestMethod]
    [DataRow("General Number", 1234.5, "1234.5")]
    [DataRow("Currency", 1234.5, "$1,234.50")]
    [DataRow("Currency", -1234.5, "-$1,234.50")]
    [DataRow("Fixed", 1234.5, "1234.50")]
    [DataRow("Standard", 1234.5, "1,234.50")]
    [DataRow("Percent", 0.1234, "12.34%")]
    [DataRow("Scientific", 1234.5, "1.23E+03")]
    [DataRow("Yes/No", 0d, "No")]
    [DataRow("Yes/No", 2d, "Yes")]
    [DataRow("True/False", -3d, "True")]
    [DataRow("On/Off", 0d, "Off")]
    [DataRow("standard", 1234.5, "1,234.50")]
    public void NamedNumberFormats(string format, double number, string expected)
        => Assert.AreEqual(expected, Formatted(V(number), format));

    [TestMethod]
    [DataRow("General Date", "3/15/2024 5:45:30 PM")]
    [DataRow("Long Date", "Friday, March 15, 2024")]
    [DataRow("Medium Date", "15-Mar-24")]
    [DataRow("Short Date", "3/15/2024")]
    [DataRow("Long Time", "5:45:30 PM")]
    [DataRow("Medium Time", "05:45 PM")]
    [DataRow("Short Time", "17:45")]
    [DataRow("SHORT DATE", "3/15/2024")]
    public void NamedDateFormats(string format, string expected)
        => Assert.AreEqual(expected, Formatted(D(2024, 3, 15, 17, 45, 30), format));

    [TestMethod]
    public void TheOfficeReferenceDateExamples()
    {
        Assert.AreEqual("17:4:23", Formatted(T(17, 4, 23), "h:m:s"));
        Assert.AreEqual("05:04:23 pm", Formatted(T(17, 4, 23), "hh:mm:ss am/pm"));
        Assert.AreEqual("05:04:23 PM", Formatted(T(17, 4, 23), "hh:mm:ss AM/PM"));
        Assert.AreEqual("Wednesday, Jan 27 1993", Formatted(D(1993, 1, 27), "dddd, mmm d yyyy"));
    }

    [TestMethod]
    [DataRow("d dd ddd dddd", "5 05 Tue Tuesday")]
    [DataRow("m mm mmm mmmm", "3 03 Mar March")]
    [DataRow("y yy yyyy q", "65 24 2024 1")]
    [DataRow("h:nn:ss", "9:07:08")]
    [DataRow("hh:mm", "09:07")]
    [DataRow("mm:ss", "03:08")]
    [DataRow("hh \"h\" mm", "09 h 07")]
    [DataRow("h A/P", "9 A")]
    [DataRow("h a/p AMPM", "9 a AM")]
    [DataRow("Am/pM", "Am")]
    [DataRow("ddddd", "3/5/2024")]
    [DataRow("dddddd", "Tuesday, March 5, 2024")]
    [DataRow("ttttt", "9:07:08 AM")]
    [DataRow("c", "3/5/2024 9:07:08 AM")]
    [DataRow("DD/MM/YYYY", "05/03/2024")]
    [DataRow("yyy", "2465")]
    public void UserDefinedDateFormats(string format, string expected)
        => Assert.AreEqual(expected, Formatted(D(2024, 3, 5, 9, 7, 8), format));

    [TestMethod]
    public void C_WritesOnlyWhatTheDateHas()
    {
        Assert.AreEqual("3/5/2024", Formatted(D(2024, 3, 5), "c"));
        Assert.AreEqual("9:07:08 AM", Formatted(T(9, 7, 8), "c"));
    }

    [TestMethod]
    public void TheHoursOfASectionWithAnAmPmSymbol_AreOfATwelveHourClock()
    {
        Assert.AreEqual("12 AM", Formatted(T(0, 0, 0), "h AM/PM"));
        Assert.AreEqual("12 PM", Formatted(T(12, 0, 0), "h AM/PM"));
        Assert.AreEqual("0", Formatted(T(0, 0, 0), "h"));
    }

    [TestMethod]
    public void ADate_IsWrittenToTheNearestSecond()
        => Assert.AreEqual("00:00:01", Formatted(V(new VBDateValue(0.6 / 86400)), "hh:nn:ss"));

    [TestMethod]
    public void TheWeek_CountsByFirstDayOfWeekAndFirstWeekOfYear()
    {
        var friday = D(2024, 3, 15);
        Assert.AreEqual("6", TextOf(Module().Format(friday, V("w"))));
        Assert.AreEqual("5", TextOf(Module().Format(friday, V("w"), VBDayOfWeek.VBMonday)));
        Assert.AreEqual("1", TextOf(Module().Format(D(2021, 1, 1), V("ww"))));
        Assert.AreEqual("53", TextOf(Module().Format(D(2021, 1, 1), V("ww"), VBDayOfWeek.VBMonday, VBFirstWeekOfYear.VBFirstFourDays)));
        Assert.AreEqual("5", TextOf(Module(1036).Format(friday, V("w"), VBDayOfWeek.VBUseSystemDayOfWeek)), "fr-FR's week begins on Monday");
    }

    [TestMethod]
    [DataRow("@@@@@", "abc", "  abc")]
    [DataRow("&&&&&", "abc", "abc")]
    [DataRow("!@@@@@", "abc", "abc  ")]
    [DataRow("@@", "abcdef", "abcdef")]
    [DataRow("!@@", "abcdef", "abcdef")]
    [DataRow("@@@-@@@-@@@@", "5551234567", "555-123-4567")]
    [DataRow("<", "HELLO", "hello")]
    [DataRow(">", "This is it", "THIS IS IT")]
    [DataRow(">@@@", "abc", "ABC")]
    [DataRow("\"[\"@\"]\"", "x", "[x]")]
    public void UserDefinedStringFormats(string format, string text, string expected)
        => Assert.AreEqual(expected, Formatted(V(text), format));

    [TestMethod]
    public void AStringFormat_WritesAnyValueAsItsString_AndAZeroLengthOneByItsSecondSection()
    {
        Assert.AreEqual("12.5", Formatted(V(12.5), "@"));
        Assert.AreEqual("empty", Formatted(V(string.Empty), "@;\"empty\""));
        Assert.AreEqual("empty", Formatted(VEmpty, "@;\"empty\""));
    }

    [TestMethod]
    public void TheRegionalSettings_AreTheEnvironments()
    {
        Assert.AreEqual("1 234 567,89", Formatted(V(1234567.891), "#,##0.00", lcid: 1036).Replace('\u202F', ' ').Replace('\u00A0', ' '));
        Assert.AreEqual("1.234.567,89", Formatted(V(1234567.891), "#,##0.00", lcid: 1031));
        Assert.AreEqual("15.03.2024", Formatted(D(2024, 3, 15), "dd/mm/yyyy", lcid: 1031));
        Assert.AreEqual("März", Formatted(D(2024, 3, 15), "mmmm", lcid: 1031));
        Assert.AreEqual("12,34,567", Formatted(V(1234567), "#,##0", lcid: 1081), "hi-IN groups by 3 then 2");
        Assert.AreEqual("2024", Formatted(D(2024, 3, 15), "yyyy", lcid: 1054), "th-TH writes its dates in the Gregorian calendar");
    }

    [TestMethod]
    public void UnderTheHijriCalendar_TheDateSymbolsCountInIt()
    {
        var session = RuntimeSessionComposer.Compose(new RuntimeEnvironmentProfile(Is64Bit: true, EnUS, 1252, SupportsOptionCompareDatabase: false), new NoSymbols());
        session.Clock.Calendar = VBCalendar.VBCalHijri;
        var module = new StdStrings(session);
        var date = D(2024, 3, 15);

        Assert.AreEqual("6/9/1445", TextOf(module.Format(date, V("d/m/yyyy"))));
        var month = TextOf(module.Format(date, V("mmm")));
        Assert.AreEqual(month, TextOf(module.Format(date, V("mmmm"))), "Hijri month names have no abbreviations");
        Assert.AreEqual("3/15/2024", TextOf(module.Format(date)), "without a format, a Let-coercion's Gregorian date");
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(module.Format(D(600, 1, 1), V("yyyy"))));
    }

    [TestMethod]
    public void Errors()
    {
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidUseOfNull, ErrorOf(Module().Format(V(1), VNull)));
        Assert.AreEqual((int)VBRuntimeErrorId.Overflow, ErrorOf(Module().Format(V(1e10), V("yyyy"))));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Module().Format(V(1), V("0"), (VBDayOfWeek)8)));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Module().Format(V(1), V("0"), VBDayOfWeek.VBSunday, (VBFirstWeekOfYear)4)));
        Assert.AreEqual((int)VBRuntimeErrorId.TypeMismatch, ErrorOf(Module().Format(VMissing, V("0"))));
        Assert.AreEqual((int)VBRuntimeErrorId.TypeMismatch, ErrorOf(Module().Format(V(new VBErrorValue(5)), V("0"))));
        Assert.AreEqual("10000000000", Formatted(V(1e10), "0"), "a number section takes any number");
    }

    [TestMethod]
    public void AFormat_IsCutTo257Characters()
        => Assert.AreEqual(new string('x', 256) + "5", Formatted(V(5), new string('x', 256) + "0yyyy"), "the year symbols past the cut would make it a date format");

    [TestMethod]
    public void FormatString_OfNull_IsError94()
    {
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidUseOfNull, ErrorOf(Module().FormatString(VNull, V("0"))));
        Assert.AreEqual("nil", Module().FormatString(VNull, V("0;0;0;\"nil\"")).Result!.Value);
    }

    [TestMethod]
    public void Format_FromSource()
    {
        CollectionAssert.AreEqual(new[] { "1,234.50", "Null", "007", "HELLO", "2024-03-05" }, Run(
            "Debug.Print Format(1234.5, \"#,##0.00\")",
            "Debug.Print Format(Null, \"0\")",
            "Debug.Print Format$(7, \"000\")",
            "Debug.Print Format(\"hello\", \">\")",
            "Debug.Print Format(#3/5/2024#, \"yyyy-mm-dd\", vbMonday, vbFirstFourDays)"));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidUseOfNull, RunToError("Debug.Print Format$(Null)"));
    }

    private sealed class NoSymbols : ISymbolProvider
    {
        public IEnumerable<Symbol> ProvideSymbols() => [];
    }
}
