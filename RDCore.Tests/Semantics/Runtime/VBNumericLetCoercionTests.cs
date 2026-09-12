using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.Tests.Semantics.Runtime;

[TestClass]
[TestCategory("RD-VBAL §5.5.1.2.1 Let-coercion between numeric types")]
public sealed class VBNumericLetCoercionTests : LetCoercionRuntimeSemanticsTests
{
    private static VBNumericLetCoercionTypeRuntimeSemantics Sut() => new(Formatter(), FakeProvider());

    [TestMethod]
    // floating -> integral : banker's rounding (MS-VBAL 5.5.1.2.1.1)
    [DataRow(2.5, (short)2)]
    [DataRow(3.5, (short)4)]
    [DataRow(2.67, (short)3)]
    [DataRow(-2.67, (short)-3)]
    public void FloatToInteger_RoundsBankers(double source, short expected)
        => AssertCoercedTo<VBIntegerValue>(Coerce(Sut(), new VBDoubleValue(source), VBIntegerType.TypeInfo), expected);

    [TestMethod]
    public void IntegerToLong_WidensAsCopy()
        => AssertCoercedTo<VBLongValue>(Coerce(Sut(), new VBIntegerValue((short)300), VBLongType.TypeInfo), 300);

    [TestMethod]
    public void IntegerToDouble_WidensAsCopy()
        => AssertCoercedTo<VBDoubleValue>(Coerce(Sut(), new VBIntegerValue((short)7), VBDoubleType.TypeInfo), 7d);

    [TestMethod]
    public void LongToInteger_InRange_NarrowsAsCopy()
        => AssertCoercedTo<VBIntegerValue>(Coerce(Sut(), new VBLongValue(1234), VBIntegerType.TypeInfo), (short)1234);

    [TestMethod]
    public void LongToInteger_OutOfRange_Overflows()
        => AssertError(Coerce(Sut(), new VBLongValue(100_000), VBIntegerType.TypeInfo), VBRuntimeErrorId.Overflow);

    // MS-VBAL 5.5.1.2.4's numeric-coercion-string grammar is documented under String, but the
    // coercion provider dispatches by destination, so it's implemented (and tested) here.
    [TestMethod]
    [DataRow("123", 123d, DisplayName = "plain digits")]
    [DataRow("123.45", 123.45d, DisplayName = "decimal point")]
    [DataRow("-123.45", -123.45d, DisplayName = "leading sign")]
    [DataRow("+5", 5d, DisplayName = "leading plus sign")]
    [DataRow("  123  ", 123d, DisplayName = "outer whitespace")]
    [DataRow("- 5", -5d, DisplayName = "whitespace between sign and digits")]
    [DataRow(".5", 0.5d, DisplayName = "no leading integer digit")]
    [DataRow("1.5E2", 150d, DisplayName = "uppercase E exponent")]
    [DataRow("1.5e+2", 150d, DisplayName = "lowercase e with explicit positive exponent sign")]
    [DataRow("1.5E-2", 0.015d, DisplayName = "negative exponent")]
    [DataRow("1D2", 100d, DisplayName = "D exponent marker (legacy BASIC notation)")]
    public void StringToDouble_ParsesNumericCoercionStringGrammar(string source, double expected)
        => AssertCoercedTo<VBDoubleValue>(Coerce(Sut(), new VBStringValue(source), VBDoubleType.TypeInfo), expected);

    [TestMethod]
    // MS-VBAL 5.5.1.2.1.1: integral destinations still apply Banker's Rounding to the parsed value.
    [DataRow("2.5", (short)2)]
    [DataRow("3.5", (short)4)]
    public void StringToInteger_RoundsBankers(string source, short expected)
        => AssertCoercedTo<VBIntegerValue>(Coerce(Sut(), new VBStringValue(source), VBIntegerType.TypeInfo), expected);

    [TestMethod]
    public void StringToInteger_OutOfRange_Overflows()
        => AssertError(Coerce(Sut(), new VBStringValue("100000"), VBIntegerType.TypeInfo), VBRuntimeErrorId.Overflow);

    [TestMethod]
    [DataRow("")]
    [DataRow("abc")]
    [DataRow("12,34")]
    [DataRow("E5")]
    public void StringToNumeric_Unparseable_IsTypeMismatch(string source)
        => AssertError(Coerce(Sut(), new VBStringValue(source), VBDoubleType.TypeInfo), VBRuntimeErrorId.TypeMismatch);
}
