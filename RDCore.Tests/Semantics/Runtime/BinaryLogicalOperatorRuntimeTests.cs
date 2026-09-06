using RDCore.Runtime.Semantics.Operators.Logical;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Intrinsic;

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
        => AssertResult<VBLongValue>(Evaluate(And(), VBLongType.TypeInfo, new VBLongValue(12), new VBLongValue(10)), 8);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.8.2 Binary 'And' Operator")]
    public void And_Integer_StaysInteger()
        => AssertResult<VBIntegerValue>(Evaluate(And(), VBIntegerType.TypeInfo, new VBIntegerValue(12), new VBIntegerValue(10)), (short)8);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.8.2 Binary 'And' Operator")]
    public void And_LongLong()
        => AssertResult<VBLongLongValue>(Evaluate(And(), VBLongLongType.TypeInfo, new VBLongLongValue(12), new VBLongLongValue(10)), 8L);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.8.2 Binary 'And' Operator")]
    public void And_Boolean()
        => AssertResult<VBBooleanValue>(Evaluate(And(), VBBooleanType.TypeInfo, new VBBooleanValue(true), new VBBooleanValue(false)), false);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.8.3 Binary 'Or' Operator")]
    public void Or_Long()
        => AssertResult<VBLongValue>(Evaluate(Or(), VBLongType.TypeInfo, new VBLongValue(12), new VBLongValue(10)), 14);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.8.3 Binary 'Or' Operator")]
    public void Or_Boolean()
        => AssertResult<VBBooleanValue>(Evaluate(Or(), VBBooleanType.TypeInfo, new VBBooleanValue(true), new VBBooleanValue(false)), true);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.8.4 Binary 'Xor' Operator")]
    public void Xor_Long()
        => AssertResult<VBLongValue>(Evaluate(Xor(), VBLongType.TypeInfo, new VBLongValue(12), new VBLongValue(10)), 6);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.8.4 Binary 'Xor' Operator")]
    public void Xor_Boolean_SameOperands_IsFalse()
        => AssertResult<VBBooleanValue>(Evaluate(Xor(), VBBooleanType.TypeInfo, new VBBooleanValue(true), new VBBooleanValue(true)), false);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.8.5 Binary 'Eqv' Operator")]
    public void Eqv_Long()
        => AssertResult<VBLongValue>(Evaluate(Eqv(), VBLongType.TypeInfo, new VBLongValue(12), new VBLongValue(10)), -7);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.8.5 Binary 'Eqv' Operator")]
    public void Eqv_Boolean_SameOperands_IsTrue()
        => AssertResult<VBBooleanValue>(Evaluate(Eqv(), VBBooleanType.TypeInfo, new VBBooleanValue(true), new VBBooleanValue(true)), true);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.8.6 Binary 'Imp' Operator")]
    public void Imp_Long()
        => AssertResult<VBLongValue>(Evaluate(Imp(), VBLongType.TypeInfo, new VBLongValue(12), new VBLongValue(10)), -5);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.8.6 Binary 'Imp' Operator")]
    public void Imp_Boolean_TrueImpliesFalse_IsFalse()
        => AssertResult<VBBooleanValue>(Evaluate(Imp(), VBBooleanType.TypeInfo, new VBBooleanValue(true), new VBBooleanValue(false)), false);
}
