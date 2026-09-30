using RDCore.Runtime.StdLib;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// <strong>MS-VBAL §6.1.2.3.1</strong> The explicit conversions to a numeric type — <c>CByte</c>, <c>CCur</c>,
/// <c>CDbl</c>, <c>CDec</c>, <c>CInt</c>, <c>CLng</c>, <c>CLngLng</c> and <c>CSng</c> — each of which is its
/// argument "Let-coerced to" the type it names.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 6.1.2.3 Conversion")]
public sealed class NumericConversionFunctionTests
{
    private static readonly StdConversion Conversion = new();

    private static VBVariantValue Variant(VBTypedValue value) => new(value);

    private static VBVariantValue Text(string value) => Variant(new VBStringValue(value));

    private static T ValueOf<T>(RuntimeSemanticsEvaluationResult<T> result) where T : VBTypedValue
    {
        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Verbose);
        return result.Result!;
    }

    private static VBRuntimeErrorId ErrorOf<T>(RuntimeSemanticsEvaluationResult<T> result) where T : VBTypedValue
    {
        Assert.IsFalse(result.IsSuccess, "expected an error");
        return (VBRuntimeErrorId)result.ErrorInfo!.ErrorId;
    }

    [TestMethod]
    public void CInt_RoundsHalfToEven()
    {
        // MS-VBAL 5.5.1.2.1: "converted to an integer using Banker's Rounding".
        Assert.AreEqual((short)2, ValueOf(Conversion.CInt(Variant(new VBDoubleValue(2.5)))).Value);
        Assert.AreEqual((short)4, ValueOf(Conversion.CInt(Variant(new VBDoubleValue(3.5)))).Value);
    }

    [TestMethod]
    public void CInt_ReadsANumericString()
        => Assert.AreEqual((short)12, ValueOf(Conversion.CInt(Text(" 12 "))).Value);

    [TestMethod]
    public void CInt_OutOfRangeIsOverflow()
        => Assert.AreEqual(VBRuntimeErrorId.Overflow, ErrorOf(Conversion.CInt(Variant(new VBLongValue(40000)))));

    [TestMethod]
    public void CInt_NonNumericStringIsTypeMismatch()
        => Assert.AreEqual(VBRuntimeErrorId.TypeMismatch, ErrorOf(Conversion.CInt(Text("abc"))));

    [TestMethod]
    public void CInt_TrueIsMinusOne()
        => Assert.AreEqual((short)-1, ValueOf(Conversion.CInt(Variant(VBBooleanValue.True))).Value);

    [TestMethod]
    public void CByte_TrueIs255()
        // MS-VBAL 5.5.1.2.2: Byte is the one destination where True is not -1.
        => Assert.AreEqual((byte)255, ValueOf(Conversion.CByte(Variant(VBBooleanValue.True))).Value);

    [TestMethod]
    public void CLng_EmptyIsZero()
        // MS-VBAL 5.5.1.2.11: "The result is 0."
        => Assert.AreEqual(0, ValueOf(Conversion.CLng(Variant(VBEmptyValue.Empty))).Value);

    [TestMethod]
    public void CLng_OfAnErrorIsItsCode()
        // MS-VBAL 6.1.2.3.1.8: "return the data value of the Long error code of the Error data value".
        => Assert.AreEqual(13, ValueOf(Conversion.CLng(Variant(new VBErrorValue(13)))).Value);

    [TestMethod]
    public void CDbl_ReadsAnExponent()
        => Assert.AreEqual(150d, ValueOf(Conversion.CDbl(Text("1.5e2"))).Value);

    [TestMethod]
    public void CSng_WidensAnInteger()
        => Assert.AreEqual(3f, ValueOf(Conversion.CSng(Variant(new VBLongValue(3)))).Value);

    [TestMethod]
    public void CCur_WidensAnInteger()
        => Assert.AreEqual(2m, Convert.ToDecimal(ValueOf(Conversion.CCur(Variant(new VBIntegerValue(2)))).RuntimeValue.BoxedValue));

    [TestMethod]
    public void CLngLng_WidensALong()
        => Assert.AreEqual(5L, ValueOf(Conversion.CLngLng(Variant(new VBLongValue(5)))).Value);

    [TestMethod]
    public void CDec_ReturnsADecimalInAVariant()
    {
        var result = ValueOf(Conversion.CDec(Text("7")));
        Assert.IsInstanceOfType<VBDecimalValue>(result.TypedValue);
        Assert.AreEqual(7m, ((VBDecimalValue)result.TypedValue).Value);
    }

    [TestMethod]
    public void CDec_OfAnErrorIsNotItsCode()
        // MS-VBAL 6.1.2.3.1.6 gives CDec no Error case, unlike every other numeric conversion.
        => Assert.AreEqual(VBRuntimeErrorId.TypeMismatch, ErrorOf(Conversion.CDec(Variant(new VBErrorValue(13)))));

    [TestMethod]
    public void TheMembersNothingImplementsYetSayTheyAreNotImplemented()
        => Assert.AreEqual(
            VBRuntimeErrorId.ApplicationDefinedOrObjectDefinedError,
            ErrorOf(Conversion.CStr(Variant(new VBLongValue(1)))));
}
