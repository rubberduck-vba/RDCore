using RDCore.Runtime.LanguageCore.StdLib.Abstract;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Values.Intrinsic;
using static RDCore.Tests.Runtime.StdLib.StdStringsTestHarness;
using RDCore.Runtime.LanguageCore.Errors;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// <strong>MS-VBAL §6.1.2.11.1.10 FormatCurrency</strong>, <strong>§6.1.2.11.1.12 FormatNumber</strong>, <strong>§6.1.2.11.1.13
/// FormatPercent</strong> and <strong>§6.1.2.11.1.11 FormatDateTime</strong>, in the regional settings of the session's environment.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 6.1.2.11.1.10 FormatCurrency")]
[TestCategory("MS-VBAL 6.1.2.11.1.11 FormatDateTime")]
[TestCategory("MS-VBAL 6.1.2.11.1.12 FormatNumber")]
[TestCategory("MS-VBAL 6.1.2.11.1.13 FormatPercent")]
public sealed class FormatNumberAndDateTimeTests
{
    private const int EnUS = 1033;
    private const VBTriState Yes = VBTriState.VBTrue;
    private const VBTriState No = VBTriState.VBFalse;
    private const VBTriState Default = VBTriState.VBUseDefault;

    private static VBVariantValue D(int year, int month, int day, int hour = 0, int minute = 0, int second = 0)
        => V(new VBDateValue(new DateTime(year, month, day, hour, minute, second).ToOADate()));

    private static VBLongValue Places(int places) => L(places);

    // ICU's en-US time patterns put a narrow no-break space before AM and PM, where Windows' put a space.
    private static string Spaced<TValue>(RDCore.SDK.Runtime.Shared.RuntimeSemanticsEvaluationResult<TValue> result)
        where TValue : RDCore.SDK.Model.Values.Abstract.VBTypedValue
        => TextOf(result).Replace('\u202F', ' ');

    [TestMethod]
    [DataRow(1234567.891, -1, Default, Default, Default, "1,234,567.89")]
    [DataRow(-1234567.891, -1, Default, Default, Default, "-1,234,567.89")]
    [DataRow(1234.5, 0, Default, Default, Default, "1,235")]
    [DataRow(1234.5, 3, Default, Default, Default, "1,234.500")]
    [DataRow(0.5, -1, No, Default, Default, ".50")]
    [DataRow(0.5, -1, Yes, Default, Default, "0.50")]
    [DataRow(-1.5, -1, Default, Yes, Default, "(1.50)")]
    [DataRow(-1.5, -1, Default, No, Default, "-1.50")]
    [DataRow(1234.5, -1, Default, Default, No, "1234.50")]
    [DataRow(-0.001, -1, Default, Yes, Default, "0.00")]
    [DataRow(2.5, 0, Default, Default, Default, "3")]
    public void FormatNumber_EnUS(double number, int places, VBTriState leading, VBTriState parentheses, VBTriState grouped, string expected)
        => Assert.AreEqual(expected, TextOf(Module(EnUS).FormatNumber(V(number), Places(places), leading, parentheses, grouped)));

    [TestMethod]
    [DataRow(1234.5, Default, "$1,234.50")]
    [DataRow(-1234.5, Default, "-$1,234.50")]
    [DataRow(-1234.5, Yes, "($1,234.50)")]
    [DataRow(-1234.5, No, "-$1,234.50")]
    public void FormatCurrency_EnUS(double number, VBTriState parentheses, string expected)
        => Assert.AreEqual(expected, TextOf(Module(EnUS).FormatCurrency(V(number), L(-1), Default, parentheses)));

    [TestMethod]
    public void FormatCurrency_InTheCulturesPatterns()
    {
        Assert.AreEqual("(¤1,234.50)", TextOf(Module().FormatCurrency(V(-1234.5), L(-1))), "the invariant culture puts a negative amount in parentheses");
        Assert.AreEqual("-¤1,234.50", TextOf(Module().FormatCurrency(V(-1234.5), L(-1), Default, No)));
        Assert.AreEqual("¥1,235", TextOf(Module(1041).FormatCurrency(V(1234.5), L(-1))), "yen have no places");
        Assert.AreEqual("₹12,34,567.89", TextOf(Module(1081).FormatCurrency(V(1234567.891), L(-1))));
        var euros = TextOf(Module(1031).FormatCurrency(V(-1234.5), L(-1), Default, Yes));
        Assert.AreEqual("(1.234,50 €)", euros.Replace('\u00A0', ' '), "the symbol where the positive pattern has it");
    }

    // MS-VBA's symbol, from Windows' locale data (NLS), for each culture whose ICU symbol differs from it on some system: the same character on
    // every system. Each was measured on Windows 11 with GetLocaleInfoEx's LOCALE_SCURRENCY; each row's comment gives the culture's ICU symbol.
    [TestMethod]
    [DataRow(1041, "\u00A5")] // ja-JP JPY, ICU on Windows and Linux: U+FFE5
    [DataRow(1152, "\u00A5")] // ug-CN CNY, ICU: U+FFE5
    [DataRow(2128, "\u00A5")] // mn-Mong-CN CNY, ICU: CN¥
    [DataRow(1028, "NT$")] // zh-TW TWD, ICU: $
    [DataRow(1133, "\u20BD")] // ba-RU RUB, ICU: RUB
    [DataRow(6203, "kr")] // sma-NO NOK, ICU: NOK
    [DataRow(7227, "kr")] // sma-SE SEK, ICU: SEK
    [DataRow(1048, "lei")] // ro-RO RON, ICU: RON
    [DataRow(14345, "Rp")] // en-ID IDR, ICU: IDR
    [DataRow(1126, "\u20A6")] // bin-NG NGN, ICU: NGN
    [DataRow(15370, "\u20B2")] // es-PY PYG, ICU: Gs.
    [DataRow(2118, "Rs")] // pa-Arab-PK PKR, ICU: U+0631
    [DataRow(1121, "\u0930\u0941")] // ne-NP NPR, ICU: U+0928 U+0947 U+0930 U+0942
    [DataRow(1090, "m.")] // tk-TM TMT, ICU: TMT
    [DataRow(11265, "\u062F.\u0627.\u200F")] // ar-JO JOD, ICU: U+062F . U+0623 . U+200F
    [DataRow(7178, "$")] // es-DO DOP, ICU: RD$
    [DataRow(1146, "$")] // arn-CL CLP, ICU: CLP
    [DataRow(1148, "$")] // moh-CA CAD, ICU: CA$
    public void FormatCurrency_WritesMSVBAsSymbol(int lcid, string symbol)
    {
        var amount = TextOf(Module(lcid).FormatCurrency(V(0), L(0)));
        Assert.AreEqual(symbol, string.Concat(amount.Where(character => character != '0' && !char.IsWhiteSpace(character))), amount);
    }

    [TestMethod]
    public void FormatPercent_IsTheNumberTimes100_WithATrailingPercentSign()
    {
        Assert.AreEqual("12.35%", TextOf(Module(EnUS).FormatPercent(V(0.12345), L(-1))));
        Assert.AreEqual("-50.00%", TextOf(Module(EnUS).FormatPercent(V(-0.5), L(-1))));
        Assert.AreEqual("(50.00%)", TextOf(Module(EnUS).FormatPercent(V(-0.5), L(-1), Default, Yes)));
        Assert.AreEqual("50%", TextOf(Module(EnUS).FormatPercent(V(0.5), L(0))));
        Assert.AreEqual("12,35%", TextOf(Module(1036).FormatPercent(V(0.12345), L(-1))));
    }

    [TestMethod]
    public void TheExpression_IsReadAsTheNumberItIs()
    {
        Assert.AreEqual("92,233,720,368,547,758.07", TextOf(Module(EnUS).FormatNumber(V(new VBDecimalValue(92233720368547758.07m)), L(-1))));
        Assert.AreEqual("1,234.50", TextOf(Module(EnUS).FormatNumber(V("1,234.5"), L(-1))));
        Assert.AreEqual("0.00", TextOf(Module(EnUS).FormatNumber(VEmpty, L(-1))));
        Assert.AreEqual("-1.00", TextOf(Module(EnUS).FormatNumber(V(true), L(-1))));
        Assert.AreEqual("43,831.00", TextOf(Module(EnUS).FormatNumber(D(2020, 1, 1), L(-1))));
    }

    [TestMethod]
    public void FormatNumber_Errors()
    {
        Assert.AreEqual((int)VBRuntimeErrorId.TypeMismatch, ErrorOf(Module().FormatNumber(V("abc"), L(-1))));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidUseOfNull, ErrorOf(Module().FormatCurrency(VNull, L(-1))));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Module().FormatPercent(V(1), L(-2))));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Module().FormatNumber(V(1), L(-1), (VBTriState)1)));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Module().FormatNumber(V(1), L(-1), Default, (VBTriState)2)));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Module().FormatNumber(V(1), L(-1), Default, Default, (VBTriState)(-3))));
        Assert.AreEqual((int)VBRuntimeErrorId.TypeMismatch, ErrorOf(Module().FormatNumber(V("abc"), L(-2))), "the coercion comes first");
    }

    [TestMethod]
    [DataRow(VBDateTimeFormat.VBGeneralDate, "3/15/2024 5:45:30 PM")]
    [DataRow(VBDateTimeFormat.VBLongDate, "Friday, March 15, 2024")]
    [DataRow(VBDateTimeFormat.VBShortDate, "3/15/2024")]
    [DataRow(VBDateTimeFormat.VBLongTime, "5:45:30 PM")]
    [DataRow(VBDateTimeFormat.VBShortTime, "17:45")]
    public void FormatDateTime_EnUS(VBDateTimeFormat format, string expected)
        => Assert.AreEqual(expected, Spaced(Module(EnUS).FormatDateTime(D(2024, 3, 15, 17, 45, 30), format)));

    [TestMethod]
    public void FormatDateTime_GeneralDate_IsHowALetCoercionWritesADate()
    {
        Assert.AreEqual("3/15/2024", Spaced(Module(EnUS).FormatDateTime(D(2024, 3, 15))));
        Assert.AreEqual("5:45:30 PM", Spaced(Module(EnUS).FormatDateTime(D(1899, 12, 30, 17, 45, 30))));
        Assert.AreEqual("15.03.2024 17:45:30", TextOf(Module(1031).FormatDateTime(D(2024, 3, 15, 17, 45, 30))));
        Assert.AreEqual("3/15/2024", Spaced(Module(EnUS).FormatDateTime(V("2024-03-15"))));
    }

    [TestMethod]
    public void FormatDateTime_Errors()
    {
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidUseOfNull, ErrorOf(Module().FormatDateTime(VNull)));
        Assert.AreEqual((int)VBRuntimeErrorId.TypeMismatch, ErrorOf(Module().FormatDateTime(V("abc"))));
        Assert.AreEqual((int)VBRuntimeErrorId.Overflow, ErrorOf(Module().FormatDateTime(V(1e10))));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Module().FormatDateTime(V(1), (VBDateTimeFormat)5)));
    }

    [TestMethod]
    public void FromSource()
    {
        CollectionAssert.AreEqual(new[] { "1,234.57", "(1.50)", "50.0%", "17:45" }, Run(
            "Debug.Print FormatNumber(1234.567)",
            "Debug.Print FormatNumber(-1.5, 2, vbUseDefault, vbTrue)",
            "Debug.Print FormatPercent(0.5, 1)",
            "Debug.Print FormatDateTime(#5:45:30 PM#, vbShortTime)"));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidUseOfNull, RunToError("Debug.Print FormatNumber(Null)"));
    }
}
