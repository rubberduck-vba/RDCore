using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Intrinsic;

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
        // 12345678f itself isn't exactly representable in a float's ~7 digits of precision, hence
        // "...567" rather than "...568" — this pins the real rounded value, not the source literal.
        => AssertCoercedTo<VBStringValue>(Coerce(Sut(), new VBSingleValue(12345678f), VBStringType.TypeInfo), "1.234567E+7");

    [TestMethod]
    public void NumericSource_NegativeSingleExceedingSevenDigits_UsesScientificNotationWithNegativeSign()
        => AssertCoercedTo<VBStringValue>(Coerce(Sut(), new VBSingleValue(-12345678f), VBStringType.TypeInfo), "-1.234567E+7");

    [TestMethod]
    [DataRow(true, "True")]
    [DataRow(false, "False")]
    public void BooleanSource_IsTrueOrFalseToken(bool source, string expected)
        => AssertCoercedTo<VBStringValue>(Coerce(Sut(), new VBBooleanValue(source), VBStringType.TypeInfo), expected);

    [TestMethod]
    public void DateSource_ZeroDate_IsTimeOnly()
        // MS-VBAL 5.5.1.2.4: "If the day value of the source date is 12/30/1899" — the day value only,
        // not the full serial (fixed 2026-09-12: this used to require the exact zero serial value,
        // which is only midnight on 12/30/1899, missing every other time of day on that same date).
        => AssertCoercedTo<VBStringValue>(
            Coerce(Sut(), new VBDateValue(0.5d), VBStringType.TypeInfo),
            new DateTime(1899, 12, 30, 12, 0, 0).ToLongTimeString());

    [TestMethod]
    public void DateSource_NonZeroDate_MidnightTime_IsShortDateOnly()
        // real VBA drops a trailing "12:00:00 AM" from a date-only value.
        => AssertCoercedTo<VBStringValue>(
            Coerce(Sut(), new VBDateValue(new DateTime(2020, 1, 1).ToOADate()), VBStringType.TypeInfo),
            new DateTime(2020, 1, 1).ToShortDateString());

    [TestMethod]
    public void DateSource_NonZeroDate_NonMidnightTime_IsShortDateAndLongTime()
    {
        // fixed 2026-09-16: a date with a real time-of-day component (e.g. 3:45:00 PM) used to lose
        // the time entirely — CStr(#1/1/2020 3:45:00 PM#) returned only "1/1/2020".
        var source = new DateTime(2020, 1, 1, 15, 45, 0);
        AssertCoercedTo<VBStringValue>(
            Coerce(Sut(), new VBDateValue(source.ToOADate()), VBStringType.TypeInfo),
            $"{source.ToShortDateString()} {source.ToLongTimeString()}");
    }

    [TestMethod]
    public void EmptySource_IsAZeroLengthString()
        // fixed 2026-09-16: see VBNumericLetCoercionTests.EmptySource_IsZero for why this was
        // unreachable (MS-VBAL 5.5.1.2.11).
        => AssertCoercedTo<VBStringValue>(Coerce(Sut(), VBEmptyValue.Empty, VBStringType.TypeInfo), "");
}
