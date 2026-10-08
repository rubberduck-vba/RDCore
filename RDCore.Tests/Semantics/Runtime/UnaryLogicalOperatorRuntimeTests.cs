using RDCore.Runtime.Semantics.Operators.Logical;
using RDCore.SDK.Model.Values.Abstract;
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
        => AssertResult<VBLongValue>(Evaluate(Not(), new VBLongValue(0)), -1);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.8.1 'Not' Operator")]
    public void Not_Long_TwelveIsMinusThirteen()
        => AssertResult<VBLongValue>(Evaluate(Not(), new VBLongValue(12)), -13);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.8.1 'Not' Operator")]
    public void Not_Integer_StaysInteger()
        => AssertResult<VBIntegerValue>(Evaluate(Not(), new VBIntegerValue(0)), (short)-1);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.8.1 'Not' Operator")]
    public void Not_LongLong()
        => AssertResult<VBLongLongValue>(Evaluate(Not(), new VBLongLongValue(0)), -1L);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.8.1 'Not' Operator")]
    public void Not_Byte()
        => AssertResult<VBByteValue>(Evaluate(Not(), new VBByteValue(0)), (byte)255);

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.8.1 'Not' Operator")]
    public void Not_Null_IsNull()
    {
        var result = Evaluate(Not(), VBNullValue.Null);
        Assert.IsNull(result.ErrorInfo);
        Assert.IsInstanceOfType<VBNullValue>(result.Result);
    }

    public static IEnumerable<object[]> NonIntegralOperands()
    {
        // 2.5 rounds to 2 (banker's rounding), and Not 2 is -3.
        yield return [new VBSingleValue(2.5f), -3];
        yield return [new VBDoubleValue(2.5), -3];
        yield return [new VBCurrencyValue(2.5m), -3];
        yield return [new VBDecimalValue(2.5m), -3];
        yield return [new VBDateValue(2), -3];
        yield return [new VBStringValue("7"), -8];
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.8.1 'Not' Operator")]
    [DynamicData(nameof(NonIntegralOperands))]
    public void Not_ANonIntegralOperand_IsTheBitwiseNotOfItAsALong(VBTypedValue operand, int expected)
        => AssertResult<VBLongValue>(Evaluate(new UnaryNotOperatorRuntimeSemantics(LetCoercionAnalysisHarness.BuildProvider(), Formatter()), operand), expected);
}
