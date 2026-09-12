using RDCore.Runtime.Semantics.Operators;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Values.Intrinsic;

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
    public void DateAndString_CoercesDateOperandToString()
    {
        var date = new VBDateValue(2);
        AssertResult<VBStringValue>(Evaluate(Concat(), date, new VBStringValue("x")), $"{date.Value.ToShortDateString()}x");
    }

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
    public void BooleanOperand_IsTypeMismatch()
        // see BinaryConcatOperatorEffectiveTypeTests.BooleanOperand_IsTypeMismatch for the spec citation.
        => AssertError(Evaluate(Concat(), new VBBooleanValue(true), new VBStringValue("x")), VBRuntimeErrorId.TypeMismatch);
}
