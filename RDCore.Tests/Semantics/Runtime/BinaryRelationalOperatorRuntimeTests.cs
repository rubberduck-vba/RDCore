using RDCore.Runtime.Semantics.Operators.Relational;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Intrinsic;

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
        => AssertResult<VBBooleanValue>(Evaluate(Eq(), VBLongType.TypeInfo, new VBLongValue(5), new VBLongValue(5)), true);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.5.1 Binary '=' Operator")]
    public void Equal_Long_False()
        => AssertResult<VBBooleanValue>(Evaluate(Eq(), VBLongType.TypeInfo, new VBLongValue(5), new VBLongValue(6)), false);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.5.1 Binary '=' Operator")]
    public void Equal_Integer_True_NoBoxedPrimitiveCast()
        // regression: (long)(object)(short) threw InvalidCastException.
        => AssertResult<VBBooleanValue>(Evaluate(Eq(), VBIntegerType.TypeInfo, new VBIntegerValue(5), new VBIntegerValue(5)), true);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.5.2 Binary '<>' Operator")]
    public void NotEqual_Long_True()
        => AssertResult<VBBooleanValue>(Evaluate(Neq(), VBLongType.TypeInfo, new VBLongValue(5), new VBLongValue(6)), true);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.5.3 Binary '<' Operator")]
    public void LessThan_Long_True()
        => AssertResult<VBBooleanValue>(Evaluate(Lt(), VBLongType.TypeInfo, new VBLongValue(5), new VBLongValue(6)), true);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.5.4 Binary '>' Operator")]
    public void GreaterThan_LongLong_Exact()
        => AssertResult<VBBooleanValue>(
            Evaluate(Gt(), VBLongLongType.TypeInfo, new VBLongLongValue(long.MaxValue), new VBLongLongValue(long.MaxValue - 1)), true);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.5.5 Binary '<=' Operator")]
    public void LessThanOrEqual_Long_EqualOperands_True()
        => AssertResult<VBBooleanValue>(Evaluate(LtEq(), VBLongType.TypeInfo, new VBLongValue(6), new VBLongValue(6)), true);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.5.6 Binary '>=' Operator")]
    public void GreaterThanOrEqual_Long_False()
        => AssertResult<VBBooleanValue>(Evaluate(GtEq(), VBLongType.TypeInfo, new VBLongValue(5), new VBLongValue(6)), false);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.5.3 Binary '<' Operator")]
    public void LessThan_Double_True()
        => AssertResult<VBBooleanValue>(Evaluate(Lt(), VBDoubleType.TypeInfo, new VBDoubleValue(1.5), new VBDoubleValue(2.5)), true);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.5.1 Binary '=' Operator")]
    public void Equal_Double_NaNOperand_IsOverflow()
        => AssertError(Evaluate(Eq(), VBDoubleType.TypeInfo, new VBDoubleValue(double.NaN), new VBDoubleValue(1.0)), VBRuntimeErrorId.Overflow);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.5.1 Binary '=' Operator")]
    public void Equal_Currency_KeepsFractionalPrecision()
        => AssertResult<VBBooleanValue>(Evaluate(Eq(), VBCurrencyType.TypeInfo, new VBCurrencyValue(1.5001m), new VBCurrencyValue(1.5001m)), true);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.5.3 Binary '<' Operator")]
    public void LessThan_Currency_True()
        => AssertResult<VBBooleanValue>(Evaluate(Lt(), VBCurrencyType.TypeInfo, new VBCurrencyValue(1.5m), new VBCurrencyValue(2.0m)), true);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.5.6 Binary '>=' Operator")]
    public void GreaterThanOrEqual_Decimal_EqualOperands_True()
        => AssertResult<VBBooleanValue>(Evaluate(GtEq(), VBDecimalType.TypeInfo, new VBDecimalValue(1.5m), new VBDecimalValue(1.5m)), true);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.5.1 Binary '=' Operator")]
    public void Equal_NullEffectiveType_ProducesNull()
    {
        var result = Evaluate(Eq(), VBNullType.TypeInfo, VBNullValue.Null, new VBLongValue(5));
        Assert.IsNull(result.ErrorInfo);
        Assert.IsInstanceOfType<VBNullValue>(result.Result);
    }
}
