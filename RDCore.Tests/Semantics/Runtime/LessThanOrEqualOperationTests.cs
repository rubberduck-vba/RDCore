using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Semantics.Runtime.Abstract;
using RDCore.SDK.Semantics.Runtime.Operators;

namespace RDCore.Tests.Semantics.Runtime;

[TestClass]
[TestCategory("MS-VBAL 5.6.9.5.5 Binary '<=' Operator")]
public class LessThanOrEqualOperationTests : BinaryOperatorOperationTests
{
    protected override BinaryOperatorSymbol Symbol => GlobalSymbols.OperatorSymbols.CompareLtEqOp;
    internal override IRuntimeSemantics Semantics => new BinaryLtEqRelationalOperatorRuntimeSemantics();
    internal override IEnumerable<VBType> EffectiveTypes => [
        VBByteType.TypeInfo,
        VBBooleanType.TypeInfo,
        VBIntegerType.TypeInfo,
        VBLongType.TypeInfo,
        VBLongLongType.TypeInfo,
        VBSingleType.TypeInfo,
        VBDoubleType.TypeInfo,
        VBCurrencyType.TypeInfo,
        VBDecimalType.TypeInfo,
        VBDateType.TypeInfo,
        VBStringType.TypeInfo,
        VBNullType.TypeInfo,
        VBErrorType.TypeInfo,
    ];

    [TestMethod]
    [DataRow(10, 20, true)]
    [DataRow(20, 10, false)]
    [DataRow(10, 10, true)]
    [DataRow(-5, 0, true)]
    public void EvaluateLessThanOrEqual_IntegerOperands_CalculatesResult(int lhs, int rhs, bool expected)
    {
        var actual = EvaluateBinaryOp(CreateContext(), lhs, rhs) as VBBooleanValue;
        Assert.AreEqual(expected, actual?.Value);
    }

    [TestMethod]
    [DataRow(1.5, 2.5, true)]
    [DataRow(2.5, 1.5, false)]
    [DataRow(1.5, 1.5, true)]
    [DataRow(-3.5, -2.5, true)]
    public void EvaluateLessThanOrEqual_DoubleOperands_CalculatesResult(double lhs, double rhs, bool expected)
    {
        var actual = EvaluateBinaryOp(CreateContext(), lhs, rhs) as VBBooleanValue;
        Assert.AreEqual(expected, actual?.Value);
    }

    [TestMethod]
    [DataRow("apple", "banana", true)]
    [DataRow("banana", "apple", false)]
    [DataRow("apple", "apple", true)]
    [DataRow("aaa", "aab", true)]
    public void EvaluateLessThanOrEqual_StringOperands_CalculatesResult(string lhs, string rhs, bool expected)
    {
        var actual = EvaluateBinaryOp(CreateContext(), lhs, rhs) as VBBooleanValue;
        Assert.AreEqual(expected, actual?.Value);
    }

    [TestMethod]
    [DataRow(true, false, true)]
    [DataRow(false, true, false)]
    [DataRow(false, false, true)]
    public void EvaluateLessThanOrEqual_BooleanOperands_CalculatesResult(bool lhs, bool rhs, bool expected)
    {
        var actual = EvaluateBinaryOp(CreateContext(), lhs, rhs) as VBBooleanValue;
        Assert.AreEqual(expected, actual?.Value);
    }

    [TestMethod]
    [DataRow(1, 2, true)]
    [DataRow(2, 1, false)]
    [DataRow(5, 5, true)]
    public void EvaluateLessThanOrEqual_ByteOperands_CalculatesResult(object lhs, object rhs, bool expected)
    {
        var actual = EvaluateBinaryOp(CreateContext(), Convert.ToByte(lhs), Convert.ToByte(rhs)) as VBBooleanValue;
        Assert.AreEqual(expected, actual?.Value);
    }
}
