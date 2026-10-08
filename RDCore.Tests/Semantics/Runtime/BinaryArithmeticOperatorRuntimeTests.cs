using RDCore.Runtime.Semantics.Operators.Arithmetic;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// Characterization matrix for binary arithmetic operator runtime semantics: the result is computed
/// in the effective type's own CLR representation, and a <c>checked</c> context surfaces integral
/// overflow as <see cref="VBRuntimeErrorId.Overflow"/> instead of a silently narrowed wrong value.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.0.2.1 Operator Evaluation")]
public sealed class BinaryArithmeticOperatorRuntimeTests : OperatorArithmeticRuntimeSemanticsTests
{
    private BinaryAdditionOperatorRuntimeSemantics Add() => new(FakeProvider(), Formatter());
    private BinarySubtractionOperatorRuntimeSematics Sub() => new(FakeProvider(), Formatter());
    private BinaryMultiplicationOperatorRuntimeSemantics Mul() => new(FakeProvider(), Formatter());
    private BinaryDivisionOperatorRuntimeSemantics Div() => new(FakeProvider(), Formatter());
    private BinaryIntegerDivisionOperatorRuntimeSemantics IntDiv() => new(FakeProvider(), Formatter());
    private BinaryModuloOperatorRuntimeSemantics Mod() => new(FakeProvider(), Formatter());
    private BinaryExponentOperatorRuntimeSemantics Pow() => new(FakeProvider(), Formatter());

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.2 Binary '+' Operator")]
    public void Addition_LongResult_KeepsLongType_NoSilentNarrowing()
        => AssertResult<VBLongValue>(
            Evaluate(Add(), new VBLongValue(20_000), new VBLongValue(20_000)), 40_000);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.2 Binary '+' Operator")]
    public void Addition_Byte_InRange()
        => AssertResult<VBByteValue>(
            Evaluate(Add(), new VBByteValue(100), new VBByteValue(100)), (byte)200);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.2 Binary '+' Operator")]
    public void Addition_Byte_Overflows()
        => AssertError(
            Evaluate(Add(), new VBByteValue(200), new VBByteValue(100)), VBRuntimeErrorId.Overflow);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.2 Binary '+' Operator")]
    public void Addition_Integer_Overflows()
        => AssertError(
            Evaluate(Add(), new VBIntegerValue(30_000), new VBIntegerValue(30_000)), VBRuntimeErrorId.Overflow);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.2 Binary '+' Operator")]
    public void Addition_Currency_KeepsFractionalPrecision()
        => AssertResult<VBCurrencyValue>(
            Evaluate(Add(), new VBCurrencyValue(1000.0001m), new VBCurrencyValue(2000.0002m)), 3000.0003m);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.2 Binary '+' Operator")]
    public void Addition_DateEffectiveType_ProducesDate()
        => AssertResult<VBDateValue>(
            Evaluate(Add(), new VBDateValue(2), new VBDateValue(3)), 5d);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.2 Binary '+' Operator")]
    public void Addition_NumericAndNull_IsNull()
        // fixed 2026-09-16: a Numeric+Null pair resolves an effective type of Null (MS-VBAL 5.6.9.3),
        // but the pipeline still tried to Let-coerce the non-null 5 operand TOWARD that Null
        // destination - no strategy handles it, so this used to surface as an internal error instead
        // of ever reaching the operator's own "Null effective type -> Null result" dispatch. Needs the
        // real coercion provider: FakeProvider's identity passthrough can't expose this - there's
        // nothing to fail to coerce.
        => AssertIsNull(Evaluate(
            new BinaryAdditionOperatorRuntimeSemantics(RealCoercionProvider(), Formatter()),
            new VBLongValue(5), VBNullValue.Null));

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.2 Binary '+' Operator")]
    public void Addition_NumericAndEmpty_TreatsEmptyAsZero()
        // fixed 2026-09-16: a Long+Empty pair resolves an effective type of Long (unlike Null, which
        // dominates to Null), so Empty genuinely needs Let-coercion to a real value - but no strategy
        // handled an Empty SOURCE (the correct MS-VBAL 5.5.1.2.11 logic existed, keyed the wrong way,
        // by an Empty DESTINATION, which never occurs). Needs the real coercion provider.
        => AssertResult<VBLongValue>(Evaluate(
            new BinaryAdditionOperatorRuntimeSemantics(RealCoercionProvider(), Formatter()),
            new VBLongValue(5), VBEmptyValue.Empty), 5);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.2 Binary '+' Operator")]
    public void Addition_VariantWrappingLong_UnwrapsBeforeEvaluating()
        // regression: a Variant operand's own TypeInfo mirrors its wrapped value's, so when the
        // effective/destination type happened to equal that same wrapped type, LetCoerceNonNullOperand
        // short-circuited coercion entirely and handed the still-boxed VBVariantValue straight to the
        // arithmetic evaluator, which crashed on its own direct cast to VBLongValue.
        => AssertResult<VBLongValue>(Evaluate(
            new BinaryAdditionOperatorRuntimeSemantics(RealCoercionProvider(), Formatter()),
            new VBVariantValue(new VBLongValue(20_000)), new VBIntegerValue(20_000)), 40_000);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.3 Binary '-' Operator")]
    public void Subtraction_Long()
        => AssertResult<VBLongValue>(
            Evaluate(Sub(), new VBLongValue(5), new VBLongValue(3)), 2);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.3 Binary '-' Operator")]
    public void Subtraction_Byte_NegativeResult_Overflows()
        => AssertError(
            Evaluate(Sub(), new VBByteValue(50), new VBByteValue(100)), VBRuntimeErrorId.Overflow);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.3 Binary '-' Operator")]
    public void Subtraction_Null_IsNull()
        => AssertIsNull(Evaluate(Sub(), VBNullValue.Null, VBNullValue.Null));

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.4 Binary '*' Operator")]
    public void Multiplication_Long()
        => AssertResult<VBLongValue>(
            Evaluate(Mul(), new VBLongValue(1_000), new VBLongValue(1_000)), 1_000_000);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.4 Binary '*' Operator")]
    public void Multiplication_Integer_Overflows()
        => AssertError(
            Evaluate(Mul(), new VBIntegerValue(300), new VBIntegerValue(300)), VBRuntimeErrorId.Overflow);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.4 Binary '*' Operator")]
    public void Multiplication_Null_IsNull()
        => AssertIsNull(Evaluate(Mul(), VBNullValue.Null, VBNullValue.Null));

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.4 Binary '*' Operator")]
    [DataRow(true)]
    [DataRow(false)]
    public void Multiplication_DateAndEmpty_IsADouble(bool dateOnTheLeft)
    {
        // MS-VBAL 5.6.9.3.4: a Date by Empty has a Double effective type, whichever side the Date is on.
        var semantics = new BinaryMultiplicationOperatorRuntimeSemantics(RealCoercionProvider(), Formatter());
        var result = dateOnTheLeft
            ? Evaluate(semantics, new VBDateValue(2), VBEmptyValue.Empty)
            : Evaluate(semantics, VBEmptyValue.Empty, new VBDateValue(2));

        AssertResult<VBDoubleValue>(result, 0d);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.7 Binary '^' Operator")]
    [DataRow(true, 1d)]
    [DataRow(false, 0.5d)]
    public void Exponent_ABooleanOperand_IsNumeric_WithADoubleEffectiveType(bool booleanOnTheLeft, double expected)
    {
        // True is -1: True ^ 2 is 1, and 2 ^ True is 0.5.
        var semantics = new BinaryExponentOperatorRuntimeSemantics(RealCoercionProvider(), Formatter());
        var result = booleanOnTheLeft
            ? Evaluate(semantics, new VBBooleanValue(true), new VBIntegerValue(2))
            : Evaluate(semantics, new VBIntegerValue(2), new VBBooleanValue(true));

        AssertResult<VBDoubleValue>(result, expected);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.6 '\\' Operator and Mod Operator")]
    public void IntegerDivision_OfASingleByABoolean_IsALong()
        // a Boolean is an operand as an Integer is: 5 \ True is 5 \ -1.
        => AssertResult<VBLongValue>(Evaluate(
            new BinaryIntegerDivisionOperatorRuntimeSemantics(RealCoercionProvider(), Formatter()), new VBSingleValue(5), new VBBooleanValue(true)), -5);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.6 '\\' Operator and Mod Operator")]
    public void Modulo_OfASingleByABoolean_IsALong()
        => AssertResult<VBLongValue>(Evaluate(
            new BinaryModuloOperatorRuntimeSemantics(RealCoercionProvider(), Formatter()), new VBSingleValue(5), new VBBooleanValue(true)), 0);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3 Arithmetic Operators")]
    [DataRow(true)]
    [DataRow(false)]
    public void Addition_OfAnErrorOperand_IsATypeMismatch(bool errorOnTheLeft)
    {
        var semantics = new BinaryAdditionOperatorRuntimeSemantics(RealCoercionProvider(), Formatter());
        var result = errorOnTheLeft
            ? Evaluate(semantics, new VBErrorValue(5), new VBLongValue(1))
            : Evaluate(semantics, new VBLongValue(1), new VBErrorValue(5));

        AssertError(result, VBRuntimeErrorId.TypeMismatch);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3 Arithmetic Operators")]
    public void Addition_OfTwoErrorOperands_IsATypeMismatch()
        => AssertError(Evaluate(new BinaryAdditionOperatorRuntimeSemantics(RealCoercionProvider(), Formatter()), new VBErrorValue(5), new VBErrorValue(5)),
            VBRuntimeErrorId.TypeMismatch);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.5 Binary '/' Operator")]
    public void Division_Double_RealQuotient()
        => AssertResult<VBDoubleValue>(
            Evaluate(Div(), new VBDoubleValue(7), new VBDoubleValue(2)), 3.5d);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.5 Binary '/' Operator")]
    public void Division_Single_RealQuotient()
        => AssertResult<VBSingleValue>(
            Evaluate(Div(), new VBSingleValue(3), new VBSingleValue(2)), 1.5f);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.5 Binary '/' Operator")]
    public void Division_ByZero_IsDivisionByZero()
        => AssertError(
            Evaluate(Div(), new VBDoubleValue(5), new VBDoubleValue(0)), VBRuntimeErrorId.DivisionByZero);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.5 Binary '/' Operator")]
    public void Division_Decimal_RealQuotient()
        => AssertResult<VBDecimalValue>(
            Evaluate(Div(), new VBDecimalValue(6m), new VBDecimalValue(4m)), 1.5m);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.5 Binary '/' Operator")]
    public void Division_Decimal_ByZero_IsDivisionByZero()
        => AssertError(
            Evaluate(Div(), new VBDecimalValue(5m), new VBDecimalValue(0m)), VBRuntimeErrorId.DivisionByZero);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.5 Binary '/' Operator")]
    public void Division_Null_IsNull()
        => AssertIsNull(Evaluate(Div(), VBNullValue.Null, VBNullValue.Null));

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.6 Binary '\\' Operator")]
    public void IntegerDivision_Long_TruncatesTowardZero()
        => AssertResult<VBLongValue>(
            Evaluate(IntDiv(), new VBLongValue(7), new VBLongValue(2)), 3);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.6 Binary '\\' Operator")]
    public void IntegerDivision_NegativeDividend_TruncatesTowardZero()
        // -7 \ 2 = -3 (nearest integer to -3.5 that is closer to zero), not -4 (floor).
        => AssertResult<VBLongValue>(
            Evaluate(IntDiv(), new VBLongValue(-7), new VBLongValue(2)), -3);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.6 Binary '\\' Operator")]
    public void IntegerDivision_ByZero_IsDivisionByZero()
        => AssertError(
            Evaluate(IntDiv(), new VBLongValue(7), new VBLongValue(0)), VBRuntimeErrorId.DivisionByZero);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.6 Binary '\\' Operator")]
    public void IntegerDivision_Null_IsNull()
        => AssertIsNull(Evaluate(IntDiv(), VBNullValue.Null, VBNullValue.Null));

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.6 'Mod' Operator")]
    public void Modulo_Long()
        => AssertResult<VBLongValue>(
            Evaluate(Mod(), new VBLongValue(7), new VBLongValue(3)), 1);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.6 'Mod' Operator")]
    public void Modulo_NegativeDividend_ResultTakesDividendSign()
        // MS-VBAL: result = left operand minus (truncated quotient * right operand); VBA's own
        // formula (a - b*(a\b)) does not take an absolute value, so -7 Mod 3 is -1, not 1.
        => AssertResult<VBLongValue>(
            Evaluate(Mod(), new VBLongValue(-7), new VBLongValue(3)), -1);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.6 'Mod' Operator")]
    public void Modulo_ByZero_IsDivisionByZero()
        => AssertError(
            Evaluate(Mod(), new VBLongValue(7), new VBLongValue(0)), VBRuntimeErrorId.DivisionByZero);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.6 'Mod' Operator")]
    public void Modulo_Null_IsNull()
        => AssertIsNull(Evaluate(Mod(), VBNullValue.Null, VBNullValue.Null));

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.7 Binary '^' Operator")]
    public void Exponent_Double()
        => AssertResult<VBDoubleValue>(
            Evaluate(Pow(), new VBDoubleValue(2), new VBDoubleValue(10)), 1024d);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.7 Binary '^' Operator")]
    public void Exponent_ZeroToZero_IsOne()
        => AssertResult<VBDoubleValue>(
            Evaluate(Pow(), new VBDoubleValue(0), new VBDoubleValue(0)), 1d);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.7 Binary '^' Operator")]
    public void Exponent_ZeroToNegative_IsInvalidProcedureCall()
        => AssertError(
            Evaluate(Pow(), new VBDoubleValue(0), new VBDoubleValue(-1)), VBRuntimeErrorId.InvalidProcedureCallOrArgument);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.7 Binary '^' Operator")]
    public void Exponent_Null_IsNull()
        => AssertIsNull(Evaluate(Pow(), VBNullValue.Null, VBNullValue.Null));

    private static void AssertIsNull(RuntimeSemanticsEvaluationResult result)
    {
        Assert.IsNull(result.ErrorInfo);
        Assert.IsInstanceOfType<VBNullValue>(result.Result);
    }
}
