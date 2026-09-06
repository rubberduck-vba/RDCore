using RDCore.Runtime.Semantics.Operators.Arithmetic;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Intrinsic;

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
            Evaluate(Add(), VBLongType.TypeInfo, new VBLongValue(20_000), new VBLongValue(20_000)), 40_000);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.2 Binary '+' Operator")]
    public void Addition_Byte_InRange()
        => AssertResult<VBByteValue>(
            Evaluate(Add(), VBByteType.TypeInfo, new VBByteValue(100), new VBByteValue(100)), (byte)200);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.2 Binary '+' Operator")]
    public void Addition_Byte_Overflows()
        => AssertError(
            Evaluate(Add(), VBByteType.TypeInfo, new VBByteValue(200), new VBByteValue(100)), VBRuntimeErrorId.Overflow);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.2 Binary '+' Operator")]
    public void Addition_Integer_Overflows()
        => AssertError(
            Evaluate(Add(), VBIntegerType.TypeInfo, new VBIntegerValue(30_000), new VBIntegerValue(30_000)), VBRuntimeErrorId.Overflow);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.2 Binary '+' Operator")]
    public void Addition_Currency_KeepsFractionalPrecision()
        => AssertResult<VBCurrencyValue>(
            Evaluate(Add(), VBCurrencyType.TypeInfo, new VBCurrencyValue(1000.0001m), new VBCurrencyValue(2000.0002m)), 3000.0003m);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.2 Binary '+' Operator")]
    public void Addition_DateEffectiveType_ProducesDate()
        => AssertResult<VBDateValue>(
            Evaluate(Add(), VBDateType.TypeInfo, new VBDoubleValue(2), new VBDoubleValue(3)), 5d);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.3 Binary '-' Operator")]
    public void Subtraction_Long()
        => AssertResult<VBLongValue>(
            Evaluate(Sub(), VBLongType.TypeInfo, new VBLongValue(5), new VBLongValue(3)), 2);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.3 Binary '-' Operator")]
    public void Subtraction_Byte_NegativeResult_Overflows()
        => AssertError(
            Evaluate(Sub(), VBByteType.TypeInfo, new VBByteValue(50), new VBByteValue(100)), VBRuntimeErrorId.Overflow);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.4 Binary '*' Operator")]
    public void Multiplication_Long()
        => AssertResult<VBLongValue>(
            Evaluate(Mul(), VBLongType.TypeInfo, new VBLongValue(1_000), new VBLongValue(1_000)), 1_000_000);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.4 Binary '*' Operator")]
    public void Multiplication_Integer_Overflows()
        => AssertError(
            Evaluate(Mul(), VBIntegerType.TypeInfo, new VBIntegerValue(300), new VBIntegerValue(300)), VBRuntimeErrorId.Overflow);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.5 Binary '/' Operator")]
    public void Division_Double_RealQuotient()
        => AssertResult<VBDoubleValue>(
            Evaluate(Div(), VBDoubleType.TypeInfo, new VBDoubleValue(7), new VBDoubleValue(2)), 3.5d);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.5 Binary '/' Operator")]
    public void Division_Single_RealQuotient()
        => AssertResult<VBSingleValue>(
            Evaluate(Div(), VBSingleType.TypeInfo, new VBSingleValue(3), new VBSingleValue(2)), 1.5f);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.5 Binary '/' Operator")]
    public void Division_ByZero_IsDivisionByZero()
        => AssertError(
            Evaluate(Div(), VBDoubleType.TypeInfo, new VBDoubleValue(5), new VBDoubleValue(0)), VBRuntimeErrorId.DivisionByZero);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.6 Binary '\\' Operator")]
    public void IntegerDivision_Long_TruncatesTowardZero()
        => AssertResult<VBLongValue>(
            Evaluate(IntDiv(), VBLongType.TypeInfo, new VBLongValue(7), new VBLongValue(2)), 3);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.6 Binary '\\' Operator")]
    public void IntegerDivision_ByZero_IsDivisionByZero()
        => AssertError(
            Evaluate(IntDiv(), VBLongType.TypeInfo, new VBLongValue(7), new VBLongValue(0)), VBRuntimeErrorId.DivisionByZero);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.6 'Mod' Operator")]
    public void Modulo_Long()
        => AssertResult<VBLongValue>(
            Evaluate(Mod(), VBLongType.TypeInfo, new VBLongValue(7), new VBLongValue(3)), 1);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.6 'Mod' Operator")]
    public void Modulo_ByZero_IsDivisionByZero()
        => AssertError(
            Evaluate(Mod(), VBLongType.TypeInfo, new VBLongValue(7), new VBLongValue(0)), VBRuntimeErrorId.DivisionByZero);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.7 Binary '^' Operator")]
    public void Exponent_Double()
        => AssertResult<VBDoubleValue>(
            Evaluate(Pow(), VBDoubleType.TypeInfo, new VBDoubleValue(2), new VBDoubleValue(10)), 1024d);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.7 Binary '^' Operator")]
    public void Exponent_ZeroToZero_IsOne()
        => AssertResult<VBDoubleValue>(
            Evaluate(Pow(), VBDoubleType.TypeInfo, new VBDoubleValue(0), new VBDoubleValue(0)), 1d);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3.7 Binary '^' Operator")]
    public void Exponent_ZeroToNegative_IsInvalidProcedureCall()
        => AssertError(
            Evaluate(Pow(), VBDoubleType.TypeInfo, new VBDoubleValue(0), new VBDoubleValue(-1)), VBRuntimeErrorId.InvalidProcedureCallOrArgument);
}
