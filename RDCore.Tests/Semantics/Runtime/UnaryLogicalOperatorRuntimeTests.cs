using RDCore.Runtime.Semantics.Operators.Logical;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// Characterization matrix for the unary <c>Not</c> operator runtime semantics: the ones-complement
/// is computed in the effective integral type's own CLR representation (RD-VBAL §5.0.2.1).
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.0.2.1 Operator Evaluation")]
public sealed class UnaryLogicalOperatorRuntimeTests : OperatorLogicalRuntimeSemanticsTests
{
    private UnaryNotOperatorRuntimeSemantics Not() => new(FakeProvider(), Formatter());

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.8.1 'Not' Operator")]
    public void Not_Long_Zero_IsMinusOne()
        => AssertResult<VBLongValue>(Evaluate(Not(), VBLongType.TypeInfo, new VBLongValue(0)), -1);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.8.1 'Not' Operator")]
    public void Not_Long_TwelveIsMinusThirteen()
        => AssertResult<VBLongValue>(Evaluate(Not(), VBLongType.TypeInfo, new VBLongValue(12)), -13);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.8.1 'Not' Operator")]
    public void Not_Integer_StaysInteger()
        => AssertResult<VBIntegerValue>(Evaluate(Not(), VBIntegerType.TypeInfo, new VBIntegerValue(0)), (short)-1);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.8.1 'Not' Operator")]
    public void Not_LongLong()
        => AssertResult<VBLongLongValue>(Evaluate(Not(), VBLongLongType.TypeInfo, new VBLongLongValue(0)), -1L);
}
