using RDCore.Runtime.Execution.Debugging;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.Tests.Runtime.Execution;

/// <summary>
/// What the debugger shows of a value: a number as the text it let-coerces to (<strong>MS-VBAL §5.5.1.2.4</strong>), as MS-VBA writes it, and not .NET's;
/// a string between double quotes, each double quote in it written twice, as its literal is (<strong>MS-VBAL §3.3.4</strong>).
/// </summary>
[TestClass]
public sealed class DebugValueFormatterTests
{
    private static readonly double PositiveNaN = BitConverter.Int64BitsToDouble(0x7FF8000000000000);

    // MS-VBA writes inf, -inf and -nan(ind) where .NET writes Infinity, -Infinity and NaN; the NaN of an invalid operation is negative.
    [TestMethod]
    [DataRow(double.PositiveInfinity, "inf", DisplayName = "positive infinity")]
    [DataRow(double.NegativeInfinity, "-inf", DisplayName = "negative infinity")]
    [DataRow(double.NaN, "-nan(ind)", DisplayName = "the NaN of an invalid operation")]
    public void ANonFiniteDouble_IsShownAsMsVbaWritesIt(double value, string expected)
        => Assert.AreEqual(expected, DebugValueFormatter.Describe(new VBDoubleValue(value)).Display);

    [TestMethod]
    public void ANaNWithNoSign_IsShownWithNone()
        => Assert.AreEqual(VBStringValue.PositiveNaN, DebugValueFormatter.Describe(new VBDoubleValue(PositiveNaN)).Display);

    [TestMethod]
    public void ANonFiniteSingle_IsShownAsMsVbaWritesIt()
        => Assert.AreEqual("-inf", DebugValueFormatter.Describe(new VBSingleValue(float.NegativeInfinity)).Display);

    [TestMethod]
    public void ANonFiniteNumberInAVariant_IsShownAsMsVbaWritesIt()
    {
        var view = DebugValueFormatter.Describe(new VBVariantValue(new VBDoubleValue(double.PositiveInfinity)));

        Assert.AreEqual("inf", view.Display);
        Assert.AreEqual("Variant/Double", view.Type);
    }

    // a number is written as MS-VBA writes it, at most 15 significant digits for a Double, with a '.' for the decimal point.
    [TestMethod]
    [DataRow(1.5, "1.5", DisplayName = "a fraction")]
    [DataRow(0.1 + 0.2, "0.3", DisplayName = "a sum MS-VBA shows to 15 digits")]
    [DataRow(-42.0, "-42", DisplayName = "a negative whole number")]
    public void AFiniteDouble_IsShownAsItsText(double value, string expected)
        => Assert.AreEqual(expected, DebugValueFormatter.Describe(new VBDoubleValue(value)).Display);

    [TestMethod]
    public void ALong_IsShownAsItsDigits()
        => Assert.AreEqual("-7", DebugValueFormatter.Describe(new VBLongValue(-7)).Display);

    // a line break is no part of a literal: the debugger shows it as it is, between the double quotes all the same.
    [TestMethod]
    [DataRow("hi", "\"hi\"", DisplayName = "a word")]
    [DataRow("say \"hi\"", "\"say \"\"hi\"\"\"", DisplayName = "a double quote inside the text, written twice")]
    [DataRow("a\r\nb", "\"a\r\nb\"", DisplayName = "a line break")]
    public void AString_IsShownBetweenDoubleQuotes(string value, string expected)
        => Assert.AreEqual(expected, DebugValueFormatter.Describe(new VBStringValue(value)).Display);
}
