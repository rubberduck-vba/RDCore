using RDCore.Runtime.Semantics.Operators.Logical;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// Characterization matrix for binary logical/bitwise operator runtime semantics: the bitwise result
/// is computed in the effective integral type's own CLR representation (RD-VBAL §5.0.2.1).
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.0.2.1 Operator Evaluation")]
public sealed class BinaryLogicalOperatorRuntimeTests : OperatorLogicalRuntimeSemanticsTests
{
    private BinaryAndLogicalOperatorRuntimeSemantics And() => new(FakeProvider(), Formatter());
    private BinaryOrLogicalOperatorRuntimeSemantics Or() => new(FakeProvider(), Formatter());
    private BinaryXorLogicalOperatorRuntimeSemantics Xor() => new(FakeProvider(), Formatter());
    private BinaryEqvLogicalOperatorRuntimeSemantics Eqv() => new(FakeProvider(), Formatter());
    private BinaryImpLogicalOperatorRuntimeSemantics Imp() => new(FakeProvider(), Formatter());

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.8.2 Binary 'And' Operator")]
    public void And_Long()
        => AssertResult<VBLongValue>(Evaluate(And(), new VBLongValue(12), new VBLongValue(10)), 8);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.8.2 Binary 'And' Operator")]
    public void And_Integer_StaysInteger()
        => AssertResult<VBIntegerValue>(Evaluate(And(), new VBIntegerValue(12), new VBIntegerValue(10)), (short)8);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.8.2 Binary 'And' Operator")]
    public void And_LongLong()
        => AssertResult<VBLongLongValue>(Evaluate(And(), new VBLongLongValue(12), new VBLongLongValue(10)), 8L);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.8.2 Binary 'And' Operator")]
    public void And_Boolean()
        => AssertResult<VBBooleanValue>(Evaluate(And(), new VBBooleanValue(true), new VBBooleanValue(false)), false);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.8.3 Binary 'Or' Operator")]
    public void Or_Long()
        => AssertResult<VBLongValue>(Evaluate(Or(), new VBLongValue(12), new VBLongValue(10)), 14);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.8.3 Binary 'Or' Operator")]
    public void Or_Boolean()
        => AssertResult<VBBooleanValue>(Evaluate(Or(), new VBBooleanValue(true), new VBBooleanValue(false)), true);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.8.4 Binary 'Xor' Operator")]
    public void Xor_Long()
        => AssertResult<VBLongValue>(Evaluate(Xor(), new VBLongValue(12), new VBLongValue(10)), 6);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.8.4 Binary 'Xor' Operator")]
    public void Xor_Boolean_SameOperands_IsFalse()
        => AssertResult<VBBooleanValue>(Evaluate(Xor(), new VBBooleanValue(true), new VBBooleanValue(true)), false);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.8.5 Binary 'Eqv' Operator")]
    public void Eqv_Long()
        => AssertResult<VBLongValue>(Evaluate(Eqv(), new VBLongValue(12), new VBLongValue(10)), -7);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.8.5 Binary 'Eqv' Operator")]
    public void Eqv_Boolean_SameOperands_IsTrue()
        => AssertResult<VBBooleanValue>(Evaluate(Eqv(), new VBBooleanValue(true), new VBBooleanValue(true)), true);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.8.6 Binary 'Imp' Operator")]
    public void Imp_Long()
        => AssertResult<VBLongValue>(Evaluate(Imp(), new VBLongValue(12), new VBLongValue(10)), -5);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.8.6 Binary 'Imp' Operator")]
    public void Imp_Boolean_TrueImpliesFalse_IsFalse()
        => AssertResult<VBBooleanValue>(Evaluate(Imp(), new VBBooleanValue(true), new VBBooleanValue(false)), false);

    // Null-propagation matrix (MS-VBAL §5.6.9.8.2-.6): each operator's own table, not a generic
    // three-valued-logic rule — And/Xor/Eqv/Imp genuinely differ in which side "absorbs".

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.8.2 Binary 'And' Operator")]
    public void And_ZeroAndNull_IsZero()
        => AssertResult<VBLongValue>(Evaluate(And(), new VBLongValue(0), VBNullValue.Null), 0);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.8.2 Binary 'And' Operator")]
    public void And_NonZeroAndNull_IsNull()
        => AssertIsNull(Evaluate(And(), new VBLongValue(12), VBNullValue.Null));

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.8.2 Binary 'And' Operator")]
    public void And_NullAndZero_IsZero()
        => AssertResult<VBLongValue>(Evaluate(And(), VBNullValue.Null, new VBLongValue(0)), 0);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.8.2 Binary 'And' Operator")]
    public void And_NullAndNonZero_IsNull()
        => AssertIsNull(Evaluate(And(), VBNullValue.Null, new VBLongValue(12)));

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.8.3 Binary 'Or' Operator")]
    public void Or_IntegralAndNull_ReturnsLeftOperandVerbatim()
        // not "only when all bits are set" — MS-VBAL specifies the left operand unconditionally.
        => AssertResult<VBLongValue>(Evaluate(Or(), new VBLongValue(5), VBNullValue.Null), 5);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.8.3 Binary 'Or' Operator")]
    public void Or_NullAndIntegral_ReturnsRightOperandVerbatim()
        => AssertResult<VBLongValue>(Evaluate(Or(), VBNullValue.Null, new VBLongValue(5)), 5);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.8.4 Binary 'Xor' Operator")]
    public void Xor_IntegralAndNull_IsNull()
        => AssertIsNull(Evaluate(Xor(), new VBLongValue(5), VBNullValue.Null));

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.8.4 Binary 'Xor' Operator")]
    public void Xor_NullAndIntegral_IsNull()
        => AssertIsNull(Evaluate(Xor(), VBNullValue.Null, new VBLongValue(5)));

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.8.5 Binary 'Eqv' Operator")]
    public void Eqv_IntegralAndNull_IsNull()
        => AssertIsNull(Evaluate(Eqv(), new VBLongValue(5), VBNullValue.Null));

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.8.5 Binary 'Eqv' Operator")]
    public void Eqv_NullAndIntegral_IsNull()
        => AssertIsNull(Evaluate(Eqv(), VBNullValue.Null, new VBLongValue(5)));

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.8.6 Binary 'Imp' Operator")]
    public void Imp_NegativeOneAndNull_IsNull()
        => AssertIsNull(Evaluate(Imp(), new VBLongValue(-1), VBNullValue.Null));

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.8.6 Binary 'Imp' Operator")]
    public void Imp_OtherThanNegativeOneAndNull_IsBitwiseImpOfLeftAndZero()
        // MS-VBAL: "Integral value other than -1, Null -> Bitwise Imp of left operand and 0".
        => AssertResult<VBIntegerValue>(Evaluate(Imp(), new VBLongValue(5), VBNullValue.Null), (short)~5);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.8.6 Binary 'Imp' Operator")]
    public void Imp_NullAndNonZero_ReturnsRightOperandVerbatim()
        => AssertResult<VBLongValue>(Evaluate(Imp(), VBNullValue.Null, new VBLongValue(5)), 5);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.8.6 Binary 'Imp' Operator")]
    public void Imp_NullAndZero_IsNull()
        => AssertIsNull(Evaluate(Imp(), VBNullValue.Null, new VBLongValue(0)));

    private static void AssertIsNull(RuntimeSemanticsEvaluationResult result)
    {
        Assert.IsNull(result.ErrorInfo);
        Assert.IsInstanceOfType<VBNullValue>(result.Result);
    }
}
