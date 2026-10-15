using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using System.Globalization;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// Characterization matrix for String let-coercion (MS-VBAL §5.5.1.2.4): the "-&gt; String" half of
/// the table (String, Numeric, Boolean and Date source), since the coercion provider dispatches by
/// destination type. "String -&gt;" (String as source, coercing to a numeric, Boolean or Date
/// destination) is owned by those destination types instead — see <see cref="VBNumericLetCoercionTests"/>,
/// <see cref="VBBooleanLetCoercionTests"/> and <see cref="VBDateLetCoercionTests"/>.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.5.1.2.4 Let-coercion to and from String")]
public sealed class VBStringLetCoercionTests : LetCoercionRuntimeSemanticsTests
{
    private static VBStringLetCoercionRuntimeSemantics Sut() => new(Formatter());

    [TestMethod]
    public void StringSource_IsACopy()
        => AssertCoercedTo<VBStringValue>(Coerce(Sut(), new VBStringValue("hello"), VBStringType.TypeInfo), "hello");

    [TestMethod]
    public void NumericSource_Zero_IsTheStringZero()
        => AssertCoercedTo<VBStringValue>(Coerce(Sut(), new VBLongValue(0), VBStringType.TypeInfo), "0");

    [TestMethod]
    [DataRow(123d, "123")]
    [DataRow(-123d, "-123")]
    [DataRow(123.45d, "123.45")]
    // fixed 2026-09-12: the sign was only ever applied to a whole-number result — a negative value
    // with a fractional part (any value that reached the "has a decimal point" branch) silently lost
    // its sign, since that branch built its string from the unsigned absoluteValue without ever
    // re-attaching the sign computed earlier.
    [DataRow(-123.45d, "-123.45")]
    public void NumericSource_NormalNotation(double source, string expected)
        => AssertCoercedTo<VBStringValue>(Coerce(Sut(), new VBDoubleValue(source), VBStringType.TypeInfo), expected);

    [TestMethod]
    public void NumericSource_RoundsToFifteenSignificantDigits()
        // fixed 2026-09-16: double's own default formatting prints the shortest round-trippable
        // representation (up to 17 significant digits) instead of rounding to Double's 15, so
        // CStr(0.1 + 0.2) used to read "0.30000000000000004".
        => AssertCoercedTo<VBStringValue>(Coerce(Sut(), new VBDoubleValue(0.1 + 0.2), VBStringType.TypeInfo), "0.3");

    [TestMethod]
    // fixed 2026-09-30: the 15-digit cap was applied by slicing the digits of a wider "E16" format,
    // which truncates where VBA rounds. Every value whose binary representation sits just below its
    // decimal one lost its last significant digit instead of carrying into it: 0.3 is stored as
    // 0.29999999999999998..., so it printed as "0.299999999999999". These sources reach the cap from
    // below, which the 0.1 + 0.2 case above (which reaches it from above) never exercised.
    [DataRow(0.3d, "0.3")]
    [DataRow(7d / 10d, "0.7")]
    [DataRow(-0.3d, "-0.3")]
    [DataRow(19.99d, "19.99")]
    public void NumericSource_FifteenthSignificantDigit_IsRoundedNotTruncated(double source, string expected)
        => AssertCoercedTo<VBStringValue>(Coerce(Sut(), new VBDoubleValue(source), VBStringType.TypeInfo), expected);

    [TestMethod]
    public void NumericSource_PositiveInfinity_IsTheInfinityToken()
        => AssertCoercedTo<VBStringValue>(Coerce(Sut(), new VBDoubleValue(double.PositiveInfinity), VBStringType.TypeInfo), VBStringValue.PositiveInfinity);

    [TestMethod]
    public void NumericSource_NegativeInfinity_IsTheNegativeInfinityToken()
        => AssertCoercedTo<VBStringValue>(Coerce(Sut(), new VBDoubleValue(double.NegativeInfinity), VBStringType.TypeInfo), VBStringValue.NegativeInfinity);

    [TestMethod]
    public void NumericSource_NaN_IsTheNaNToken()
        => AssertCoercedTo<VBStringValue>(Coerce(Sut(), new VBDoubleValue(double.NaN), VBStringType.TypeInfo), VBStringValue.NaN);

    [TestMethod]
    public void NumericSource_ExceedsSignificantIntegerDigits_UsesScientificNotationWithSignedExponent()
        // MS-VBAL 5.5.1.2.4: the exponent is always signed ("+" or "-") — a bare C# int.ToString()
        // would have produced "E20" instead of "E+20" for a positive exponent. Exact string (not just
        // a "Contains E+" substring check) so a mantissa or missing-sign mutation can't survive.
        => AssertCoercedTo<VBStringValue>(Coerce(Sut(), new VBDoubleValue(100_000_000_000_000_000_000d), VBStringType.TypeInfo), "1E+20");

    [TestMethod]
    public void NumericSource_NegativeExceedsSignificantIntegerDigits_UsesScientificNotationWithNegativeSign()
        // the sign branch of scientific notation had no dedicated coverage before this.
        => AssertCoercedTo<VBStringValue>(Coerce(Sut(), new VBDoubleValue(-100_000_000_000_000_000_000d), VBStringType.TypeInfo), "-1E+20");

    [TestMethod]
    public void NumericSource_SingleExceedingSevenDigits_UsesScientificNotation()
        // Single's significant-digit threshold (7) is lower than Double's (15), so a value that's
        // still normal notation for a Double must go scientific when the source is a Single.
        // The expectation was "1.234567E+7" until 2026-09-30: 12345678 is below 2^24 and so is exact
        // in a float, and the 8th digit is dropped by the 7-digit cap — which rounds, not truncates.
        => AssertCoercedTo<VBStringValue>(Coerce(Sut(), new VBSingleValue(12345678f), VBStringType.TypeInfo), "1.234568E+7");

    [TestMethod]
    public void NumericSource_NegativeSingleExceedingSevenDigits_UsesScientificNotationWithNegativeSign()
        => AssertCoercedTo<VBStringValue>(Coerce(Sut(), new VBSingleValue(-12345678f), VBStringType.TypeInfo), "-1.234568E+7");

    [TestMethod]
    [DataRow(true, "True")]
    [DataRow(false, "False")]
    public void BooleanSource_IsTrueOrFalseToken(bool source, string expected)
        => AssertCoercedTo<VBStringValue>(Coerce(Sut(), new VBBooleanValue(source), VBStringType.TypeInfo), expected);

    // the dates below are written in the invariant culture, which is the regional settings of a strategy given none: the Long
    // Time format is HH:mm:ss, and the Short Date format MM/dd/yyyy.

    [TestMethod]
    public void DateSource_ZeroDate_IsTimeOnly()
        // MS-VBAL 5.5.1.2.4: "If the day value of the source date is 12/30/1899" — the day value only,
        // not the full serial (fixed 2026-09-12: this used to require the exact zero serial value,
        // which is only midnight on 12/30/1899, missing every other time of day on that same date).
        => AssertCoercedTo<VBStringValue>(Coerce(Sut(), new VBDateValue(0.5d), VBStringType.TypeInfo), "12:00:00");

    [TestMethod]
    public void DateSource_NonZeroDate_MidnightTime_IsShortDateOnly()
        // real VBA drops a trailing "12:00:00 AM" from a date-only value.
        => AssertCoercedTo<VBStringValue>(
            Coerce(Sut(), new VBDateValue(new DateTime(2020, 1, 1).ToOADate()), VBStringType.TypeInfo), "01/01/2020");

    [TestMethod]
    public void DateSource_NonZeroDate_NonMidnightTime_IsShortDateAndLongTime()
        // fixed 2026-09-16: a date with a real time-of-day component (e.g. 3:45:00 PM) used to lose
        // the time entirely — CStr(#1/1/2020 3:45:00 PM#) returned only "1/1/2020".
        => AssertCoercedTo<VBStringValue>(
            Coerce(Sut(), new VBDateValue(new DateTime(2020, 1, 1, 15, 45, 0).ToOADate()), VBStringType.TypeInfo),
            "01/01/2020 15:45:00");

    // MS-VBAL 5.5.1.2.4 writes a number "using the host-defined regional decimal symbol" and a date in "the Short Date format
    // [and] the Long Time format of the host-defined regional settings": the environment's culture, never the thread's.

    [TestMethod]
    public void DateSource_IsFormattedByTheEnvironmentsCulture()
        => AssertCoercedTo<VBStringValue>(
            Coerce(new VBStringLetCoercionRuntimeSemantics(Formatter(), CultureInfo.GetCultureInfo(1031)),
                new VBDateValue(new DateTime(2020, 1, 1, 15, 45, 0).ToOADate()), VBStringType.TypeInfo),
            "01.01.2020 15:45:00");

    [TestMethod]
    [DataRow(123.45d, "123,45")]
    [DataRow(-0.5d, "-0,5")]
    public void NumericSource_UsesTheEnvironmentsDecimalSymbol(double source, string expected)
        => AssertCoercedTo<VBStringValue>(
            Coerce(new VBStringLetCoercionRuntimeSemantics(Formatter(), CultureInfo.GetCultureInfo(1031)), new VBDoubleValue(source), VBStringType.TypeInfo),
            expected);

    [TestMethod]
    public void NumericSource_InScientificNotation_UsesTheEnvironmentsDecimalSymbol()
        => AssertCoercedTo<VBStringValue>(
            Coerce(new VBStringLetCoercionRuntimeSemantics(Formatter(), CultureInfo.GetCultureInfo(1031)), new VBSingleValue(12345678f), VBStringType.TypeInfo),
            "1,234568E+7");

    [TestMethod]
    public void DateSource_DoesNotDependOnTheCultureOfTheCallingThread()
    {
        var culture = (CultureInfo)CultureInfo.GetCultureInfo("en-US").Clone();
        culture.DateTimeFormat.ShortDatePattern = "yyyy|MM|dd";
        culture.DateTimeFormat.TimeSeparator = ".";
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = culture;
        try
        {
            AssertCoercedTo<VBStringValue>(
                Coerce(Sut(), new VBDateValue(new DateTime(2020, 1, 1, 15, 45, 0).ToOADate()), VBStringType.TypeInfo),
                "01/01/2020 15:45:00");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [TestMethod]
    public void DateSource_UnderACultureWithAnotherCalendar_IsInTheGregorianCalendar()
    {
        // th-TH may write in the Thai Buddhist calendar, where 2020 is 2563; the culture's patterns are kept, its calendar not.
        var text = ((VBStringValue)Coerce(new VBStringLetCoercionRuntimeSemantics(Formatter(), CultureInfo.GetCultureInfo(1054)),
            new VBDateValue(new DateTime(2020, 1, 15).ToOADate()), VBStringType.TypeInfo).Result!).Value;

        Assert.Contains("2020", text);
        Assert.DoesNotContain("2563", text);
    }

    [TestMethod]
    public void EmptySource_IsAZeroLengthString()
        // fixed 2026-09-16: see VBNumericLetCoercionTests.EmptySource_IsZero for why this was
        // unreachable (MS-VBAL 5.5.1.2.11).
        => AssertCoercedTo<VBStringValue>(Coerce(Sut(), VBEmptyValue.Empty, VBStringType.TypeInfo), "");

    // "a copy of the source string" is a copy of all of its data, not of the characters a managed string can hold.

    [TestMethod]
    public void StringSource_KeepsItsTrailingByte()
    {
        var result = (VBStringValue)Coerce(Sut(), VBStringValue.FromBytes([0x41, 0x00, 0x42]), VBStringType.TypeInfo).Result!;

        Assert.AreEqual("A", result.Value);
        Assert.AreEqual((byte)0x42, result.TrailingByte);
    }

    [TestMethod]
    public void StringSource_TheNullString_StaysTheNullString()
        => Assert.IsTrue(((VBStringValue)Coerce(Sut(), VBStringValue.VBNullString, VBStringType.TypeInfo).Result!).IsNullString);

    [TestMethod]
    [TestCategory("RD-VBAL §5.5.1.2.6 Let-coercion to and from a resizable Byte array")]
    public void ByteArraySource_AnOddCount_KeepsTheTrailingByte()
    {
        var result = (VBStringValue)Coerce(Sut(), Bytes(0x41, 0x00, 0x42), VBStringType.TypeInfo).Result!;

        Assert.AreEqual(3, result.ByteLength);
        CollectionAssert.AreEqual(new byte[] { 0x41, 0x00, 0x42 }, result.GetBytes());
    }

    [TestMethod]
    [TestCategory("RD-VBAL §5.5.1.2.6 Let-coercion to and from a resizable Byte array")]
    public void ByteArraySource_ALoneSurrogate_IsNotReplaced()
        // 00 D8 is the code unit U+D800 on its own, which a text encoding reads as U+FFFD instead.
        => AssertCoercedTo<VBStringValue>(Coerce(Sut(), Bytes(0x00, 0xD8), VBStringType.TypeInfo), "\uD800");

    private static VBResizableByteArrayValue Bytes(params byte[] bytes)
    {
        var array = new VBResizableByteArrayValue([(0, bytes.Length - 1)]);
        for (var index = 0; index < bytes.Length; index++)
        {
            array.TrySetElement(new ValueBindingHandle(new VBRuntimeValue<byte>(bytes[index])), index);
        }
        return array;
    }
}
