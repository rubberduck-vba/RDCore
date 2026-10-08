using RDCore.Runtime.Semantics.Operators.Relational;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// Characterization matrix for binary relational operator runtime semantics: the comparison is exact
/// in the effective type's own representation and yields a <see cref="VBBooleanValue"/>
/// (RD-VBAL §5.0.2.1).
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.0.2.1 Operator Evaluation")]
public sealed class BinaryRelationalOperatorRuntimeTests : OperatorRelationalRuntimeSemanticsTests
{
    private BinaryEqRelationalOperatorRuntimeSemantics Eq() => new(FakeProvider(), Formatter());
    private BinaryNeqRelationalOperatorRuntimeSemantics Neq() => new(FakeProvider(), Formatter());
    private BinaryLtRelationalOperatorRuntimeSemantics Lt() => new(FakeProvider(), Formatter());
    private BinaryGtRelationalOperatorRuntimeSemantics Gt() => new(FakeProvider(), Formatter());
    private BinaryLtEqRelationalOperatorRuntimeSemantics LtEq() => new(FakeProvider(), Formatter());
    private BinaryGtEqRelationalOperatorRuntimeSemantics GtEq() => new(FakeProvider(), Formatter());

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.5.1 Binary '=' Operator")]
    public void Equal_Long_True()
        => AssertResult<VBBooleanValue>(Evaluate(Eq(), new VBLongValue(5), new VBLongValue(5)), true);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.5.1 Binary '=' Operator")]
    public void Equal_Long_False()
        => AssertResult<VBBooleanValue>(Evaluate(Eq(), new VBLongValue(5), new VBLongValue(6)), false);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.5.1 Binary '=' Operator")]
    public void Equal_Integer_True_NoBoxedPrimitiveCast()
        // regression: (long)(object)(short) threw InvalidCastException.
        => AssertResult<VBBooleanValue>(Evaluate(Eq(), new VBIntegerValue(5), new VBIntegerValue(5)), true);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.5.1 Binary '=' Operator")]
    public void Equal_VariantWrappingLong_UnwrapsBeforeComparing()
        // same short-circuit bug as the arithmetic operators: a Variant operand's own TypeInfo mirrors
        // its wrapped value's, so LetCoerceNonNullOperand skipped coercion and handed the evaluator a
        // still-boxed VBVariantValue instead of the VBLongValue its own direct cast expects.
        => AssertResult<VBBooleanValue>(Evaluate(
            new BinaryEqRelationalOperatorRuntimeSemantics(RealCoercionProvider(), Formatter()),
            new VBVariantValue(new VBLongValue(5)), new VBLongValue(5)), true);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.5.3 Binary '<' Operator")]
    public void LessThan_VariantNumericVsVariantString_NumericIsAlwaysLess()
        // MS-VBAL 5.6.9.5's own exception: when both operands are Variant, one originally holding a
        // numeric value and the other a String, the numeric operand is ALWAYS considered less than the
        // String operand, regardless of actual values - 999 < "3" is True here despite 999 > 3
        // numerically, proving this isn't a normal coercion+comparison in disguise.
        => AssertResult<VBBooleanValue>(Evaluate(Lt(),
            new VBVariantValue(new VBLongValue(999)), new VBVariantValue(new VBStringValue("3"))), true);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.5.4 Binary '>' Operator")]
    public void GreaterThan_VariantStringVsVariantNumeric_StringIsAlwaysGreater()
        => AssertResult<VBBooleanValue>(Evaluate(Gt(),
            new VBVariantValue(new VBStringValue("3")), new VBVariantValue(new VBLongValue(999))), true);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.5.1 Binary '=' Operator")]
    public void Equal_VariantNumericVsVariantString_NeverEqual_EvenWithMatchingDigits()
        // same numeric text on both sides - a normal comparison (even a string-vs-string one) would
        // call these equal; the exception says a numeric-vs-String Variant pair never is.
        => AssertResult<VBBooleanValue>(Evaluate(Eq(),
            new VBVariantValue(new VBLongValue(999)), new VBVariantValue(new VBStringValue("999"))), false);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.5.1 Binary '=' Operator")]
    public void Equal_BothVariantsNumeric_UsesNormalComparison_NotTheException()
        // guards the detection itself: two Variants both holding numerics must NOT trigger the
        // String/Numeric exception - ordinary numeric equality still applies (needs the real coercion
        // provider: this path unwraps both Variants for real, which FakeProvider's identity
        // passthrough doesn't do).
        => AssertResult<VBBooleanValue>(Evaluate(
            new BinaryEqRelationalOperatorRuntimeSemantics(RealCoercionProvider(), Formatter()),
            new VBVariantValue(new VBLongValue(5)), new VBVariantValue(new VBLongValue(5))), true);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.5.2 Binary '<>' Operator")]
    public void NotEqual_Long_True()
        => AssertResult<VBBooleanValue>(Evaluate(Neq(), new VBLongValue(5), new VBLongValue(6)), true);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.5.3 Binary '<' Operator")]
    public void LessThan_Long_True()
        => AssertResult<VBBooleanValue>(Evaluate(Lt(), new VBLongValue(5), new VBLongValue(6)), true);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.5.4 Binary '>' Operator")]
    public void GreaterThan_LongLong_Exact()
        => AssertResult<VBBooleanValue>(
            Evaluate(Gt(), new VBLongLongValue(long.MaxValue), new VBLongLongValue(long.MaxValue - 1)), true);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.5.5 Binary '<=' Operator")]
    public void LessThanOrEqual_Long_EqualOperands_True()
        => AssertResult<VBBooleanValue>(Evaluate(LtEq(), new VBLongValue(6), new VBLongValue(6)), true);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.5.6 Binary '>=' Operator")]
    public void GreaterThanOrEqual_Long_False()
        => AssertResult<VBBooleanValue>(Evaluate(GtEq(), new VBLongValue(5), new VBLongValue(6)), false);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.5.3 Binary '<' Operator")]
    public void LessThan_Double_True()
        => AssertResult<VBBooleanValue>(Evaluate(Lt(), new VBDoubleValue(1.5), new VBDoubleValue(2.5)), true);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.5.1 Binary '=' Operator")]
    public void Equal_Double_NaNOperand_IsOverflow()
        => AssertError(Evaluate(Eq(), new VBDoubleValue(double.NaN), new VBDoubleValue(1.0)), VBRuntimeErrorId.Overflow);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.5.1 Binary '=' Operator")]
    public void Equal_Currency_KeepsFractionalPrecision()
        => AssertResult<VBBooleanValue>(Evaluate(Eq(), new VBCurrencyValue(1.5001m), new VBCurrencyValue(1.5001m)), true);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.5.3 Binary '<' Operator")]
    public void LessThan_Currency_True()
        => AssertResult<VBBooleanValue>(Evaluate(Lt(), new VBCurrencyValue(1.5m), new VBCurrencyValue(2.0m)), true);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.5.6 Binary '>=' Operator")]
    public void GreaterThanOrEqual_Decimal_EqualOperands_True()
        => AssertResult<VBBooleanValue>(Evaluate(GtEq(), new VBDecimalValue(1.5m), new VBDecimalValue(1.5m)), true);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.5.1 Binary '=' Operator")]
    public void Equal_NullEffectiveType_ProducesNull()
    {
        var result = Evaluate(Eq(), VBNullValue.Null, new VBLongValue(5));
        Assert.IsNull(result.ErrorInfo);
        Assert.IsInstanceOfType<VBNullValue>(result.Result);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.5.1 Binary '=' Operator")]
    public void Equal_NumericAndNull_IsNull()
        // fixed 2026-09-16: same latent bug as the arithmetic operators' Numeric+Null case - the
        // pipeline tried to Let-coerce the non-null 5 operand toward the Null effective type before
        // this operator's own Null dispatch was ever reached. Needs the real coercion provider:
        // FakeProvider's identity passthrough can't expose this.
        => AssertIsNullResult(Evaluate(
            new BinaryEqRelationalOperatorRuntimeSemantics(RealCoercionProvider(), Formatter()),
            new VBLongValue(5), VBNullValue.Null));

    private static void AssertIsNullResult(RuntimeSemanticsEvaluationResult result)
    {
        Assert.IsNull(result.ErrorInfo);
        Assert.IsInstanceOfType<VBNullValue>(result.Result);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.5.1 Binary '=' Operator")]
    public void Equal_String_True()
        => AssertResult<VBBooleanValue>(Evaluate(Eq(), new VBStringValue("abc"), new VBStringValue("abc")), true);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.5.1 Binary '=' Operator")]
    public void Equal_String_CaseSensitive_False()
        // Binary compare (this module's default, absent Option Compare Text) is case-sensitive.
        => AssertResult<VBBooleanValue>(Evaluate(Eq(), new VBStringValue("abc"), new VBStringValue("ABC")), false);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.5.3 Binary '<' Operator")]
    public void LessThan_String_LexicographicallyBefore_True()
        => AssertResult<VBBooleanValue>(Evaluate(Lt(), new VBStringValue("abc"), new VBStringValue("abd")), true);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.5.2 Binary '<>' Operator")]
    public void NotEqual_String_DifferentValues_True()
        => AssertResult<VBBooleanValue>(Evaluate(Neq(), new VBStringValue("abc"), new VBStringValue("xyz")), true);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.5.1 Binary '=' Operator")]
    public void Equal_Boolean_SameValue_True()
        => AssertResult<VBBooleanValue>(Evaluate(Eq(), new VBBooleanValue(true), new VBBooleanValue(true)), true);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.5.1 Binary '=' Operator")]
    public void Equal_Boolean_DifferentValues_False()
        => AssertResult<VBBooleanValue>(Evaluate(Eq(), new VBBooleanValue(true), new VBBooleanValue(false)), false);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.5.3 Binary '<' Operator")]
    public void LessThan_Boolean_FalseIsGreaterThanTrue()
        // Boolean compares over its -1 (True) / 0 (False) representation: True < False.
        => AssertResult<VBBooleanValue>(Evaluate(Lt(), new VBBooleanValue(true), new VBBooleanValue(false)), true);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.5.1 Binary '=' Operator")]
    public void Equal_Error_SameStandardCode_True()
        // MS-VBAL 5.6.9.5: two standard error codes compare by their numeric value.
        => AssertResult<VBBooleanValue>(Evaluate(Eq(), new VBErrorValue(5), new VBErrorValue(5)), true);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.5.1 Binary '=' Operator")]
    public void Equal_Error_DifferentStandardCode_False()
        => AssertResult<VBBooleanValue>(Evaluate(Eq(), new VBErrorValue(5), new VBErrorValue(9)), false);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.5.3 Binary '<' Operator")]
    public void LessThan_Error_ComparesNumericValue_True()
        => AssertResult<VBBooleanValue>(Evaluate(Lt(), new VBErrorValue(5), new VBErrorValue(9)), true);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.5.3 Binary '<' Operator")]
    public void LessThan_Date_ComparesTheDatesAsDoubles()
        => AssertResult<VBBooleanValue>(Evaluate(
            new BinaryLtRelationalOperatorRuntimeSemantics(RealCoercionProvider(), Formatter()), new VBDateValue(2), new VBDateValue(3)), true);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.5.1 Binary '=' Operator")]
    public void Equal_DateAndLong_ComparesTheDateAsADouble()
        => AssertResult<VBBooleanValue>(Evaluate(
            new BinaryEqRelationalOperatorRuntimeSemantics(RealCoercionProvider(), Formatter()), new VBDateValue(2), new VBLongValue(2)), true);
}
