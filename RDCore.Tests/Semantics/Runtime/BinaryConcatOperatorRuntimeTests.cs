using RDCore.Runtime.Semantics.Operators;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// Characterization matrix for the <c>&amp;</c> (concatenation) operator runtime semantics
/// (MS-VBAL §5.6.9.4): non-<c>Null</c> operands are let-coerced to the operator's String value type,
/// then concatenated; a value type of <c>Null</c> (both operands <c>Null</c>) propagates <c>Null</c>.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.0.2.1 Operator Evaluation")]
[TestCategory("MS-VBAL 5.6.9.4 & Operator")]
public sealed class BinaryConcatOperatorRuntimeTests : OperatorConcatRuntimeSemanticsTests
{
    private BinaryConcatOperatorRuntimeSemantics Concat() => new(StringCoercionProvider(), Formatter());

    [TestMethod]
    public void StringAndString_ConcatenatesVerbatim()
        => AssertResult<VBStringValue>(Evaluate(Concat(), new VBStringValue("ab"), new VBStringValue("cd")), "abcd");

    [TestMethod]
    public void NumericAndString_CoercesNumericOperandToString()
        => AssertResult<VBStringValue>(Evaluate(Concat(), new VBLongValue(5), new VBStringValue("x")), "5x");

    [TestMethod]
    public void StringAndNumeric_CoercesNumericOperandToString()
        => AssertResult<VBStringValue>(Evaluate(Concat(), new VBStringValue("x"), new VBLongValue(5)), "x5");

    [TestMethod]
    public void VariantWrappingString_UnwrapsBeforeConcatenating()
        // same short-circuit bug as the arithmetic/relational operators: concat's own destination type
        // is always String, so a Variant operand whose own wrapped value already is a String is the one
        // shape that hits LetCoerceNonNullOperand's TypeInfo-equality short-circuit here.
        => AssertResult<VBStringValue>(Evaluate(Concat(), new VBVariantValue(new VBStringValue("x")), new VBStringValue("y")), "xy");

    [TestMethod]
    public void DateAndString_CoercesDateOperandToString()
        // the date written in the regional settings of the String coercion, which are invariant here.
        => AssertResult<VBStringValue>(Evaluate(Concat(), new VBDateValue(2), new VBStringValue("x")), "01/01/1900x");

    [TestMethod]
    public void NullAndNull_IsNull()
    {
        var result = Evaluate(Concat(), VBNullValue.Null, VBNullValue.Null);
        Assert.IsNull(result.ErrorInfo);
        Assert.IsInstanceOfType<VBNullValue>(result.Result);
    }

    // A lone Null operand still resolves the operator's value type to String (MS-VBAL's value-type
    // table pairs Null on one side with non-Null on the other in that same "-> String" row) — but the
    // pipeline exempts Null operands from let-coercion regardless of the effective type (same rule for
    // every simple data operator, RD-VBAL 5.6.9.2/5.6.9.4). So EvaluateExpressionResult must treat a
    // surviving VBNullValue operand as an empty string rather than assume every String-effective-type
    // operand already IS a VBStringValue.
    // Regression: this used to throw InvalidCastException instead of returning a VBStringValue.
    [TestMethod]
    public void NullAndString_NullOperandContributesEmptyString()
        => AssertResult<VBStringValue>(Evaluate(Concat(), VBNullValue.Null, new VBStringValue("x")), "x");

    [TestMethod]
    public void StringAndNull_NullOperandContributesEmptyString()
        => AssertResult<VBStringValue>(Evaluate(Concat(), new VBStringValue("x"), VBNullValue.Null), "x");

    [TestMethod]
    public void NullAndNumeric_NullOperandContributesEmptyString()
        => AssertResult<VBStringValue>(Evaluate(Concat(), VBNullValue.Null, new VBLongValue(5)), "5");

    [TestMethod]
    public void BooleanAndString_ConcatenatesTheBooleansToken()
        // see BinaryConcatOperatorEffectiveTypeTests.BooleanOperand_IsString: MS-VBA's "x" & True is "xTrue".
        => AssertResult<VBStringValue>(Evaluate(Concat(), new VBBooleanValue(true), new VBStringValue("x")), "Truex");

    [TestMethod]
    public void StringAndBoolean_ConcatenatesTheBooleansToken()
        => AssertResult<VBStringValue>(Evaluate(Concat(), new VBStringValue("x"), new VBBooleanValue(false)), "xFalse");

    // MS-VBAL 5.6.9.4: "the result is the left operand's data followed by the right operand's" - all of their bytes, so
    // a trailing byte on the left is the low byte of the first character after it.

    [TestMethod]
    public void StringWithATrailingByte_AndAnother_PairTheirBytes()
    {
        var result = (VBStringValue)Evaluate(Concat(), VBStringValue.FromBytes([0x41]), VBStringValue.FromBytes([0x42])).Result!;

        Assert.AreEqual("\u4241", result.Value);
        Assert.IsNull(result.TrailingByte);
    }

    [TestMethod]
    public void StringWithATrailingByte_AndText_ShiftsTheText()
        // 41 then "x" (78 00) is 41 78 00: the character U+7841 and a trailing 00.
        => CollectionAssert.AreEqual(new byte[] { 0x41, 0x78, 0x00 },
            ((VBStringValue)Evaluate(Concat(), VBStringValue.FromBytes([0x41]), new VBStringValue("x")).Result!).GetBytes());

    [TestMethod]
    public void ByteArrayAndByteArray_OneByteEach_ConcatenateByteForByte()
    {
        // each array is a String of one byte, which is no character at all; together they are one.
        var result = (VBStringValue)Evaluate(Concat(), ByteArrayOf(0x41), ByteArrayOf(0x42)).Result!;

        Assert.AreEqual(2, result.ByteLength);
        Assert.AreEqual("\u4241", result.Value);
    }

    [TestMethod]
    public void Null_AndAStringWithATrailingByte_KeepsTheByte()
        => Assert.AreEqual(1, ((VBStringValue)Evaluate(Concat(), VBNullValue.Null, VBStringValue.FromBytes([0x41])).Result!).ByteLength);

    private static VBResizableByteArrayValue ByteArrayOf(params byte[] bytes)
    {
        var array = new VBResizableByteArrayValue([(0, bytes.Length - 1)]);
        for (var index = 0; index < bytes.Length; index++)
        {
            array.TrySetElement(new ValueBindingHandle(new VBRuntimeValue<byte>(bytes[index])), index);
        }
        return array;
    }

    [TestMethod]
    public void ByteArrayAndByteArray_UninitializedArrays_ConcatenateToEmptyString()
        // MS-VBAL 5.5.1.2.6: "If the byte array is uninitialized, the result is a 0-length string."
        => AssertResult<VBStringValue>(Evaluate(Concat(), VBResizableByteArrayValue.Empty, VBResizableByteArrayValue.Empty), "");

    [TestMethod]
    public void VariantWrappingAByteArray_AndByteArray_ConcatenateToEmptyString()
        // regression: IsByteArray's own pattern match needs the real wrapped array, not the
        // VBVariantValue box around it - a Variant holding a Byte array used to fail the check entirely.
        => AssertResult<VBStringValue>(Evaluate(Concat(), new VBVariantValue(VBResizableByteArrayValue.Empty), VBResizableByteArrayValue.Empty), "");
}
