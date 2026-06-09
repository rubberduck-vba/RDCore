using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Semantics.Runtime.Abstract;
using RDCore.SDK.Semantics.Runtime.Operators;

namespace RDCore.Tests.Semantics.Runtime;

[TestClass]
[TestCategory("MS-VBAL 5.6.9.5.6 Binary '>=' Operator")]
public class GreaterThanOrEqualOperationTests : BinaryOperatorOperationTests
{
    protected override BinaryOperatorSymbol Symbol => GlobalSymbols.OperatorSymbols.CompareGtEqOp;
    internal override IRuntimeSemantics Semantics => new BinaryGtEqRelationalOperatorRuntimeSemantics();
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
    [DataRow(20, 10, true)]
    [DataRow(10, 20, false)]
    [DataRow(10, 10, true)]
    [DataRow(0, -5, true)]
    public void Operator_IntegerContext_EvaluatesOp(int lhs, int rhs, bool expected)
    {
        var actual = EvaluateBinaryOp(CreateContext(), lhs, rhs) as VBBooleanValue;
        Assert.AreEqual(expected, actual?.Value);
    }

    [TestMethod]
    [DataRow(2.5, 1.5, true)]
    [DataRow(1.5, 2.5, false)]
    [DataRow(1.5, 1.5, true)]
    [DataRow(-2.5, -3.5, true)]
    public void Operator_DoubleContext_EvaluatesOp(double lhs, double rhs, bool expected)
    {
        var actual = EvaluateBinaryOp(CreateContext(), lhs, rhs) as VBBooleanValue;
        Assert.AreEqual(expected, actual?.Value);
    }

    [TestMethod]
    [DataRow("banana", "apple", true)]
    [DataRow("apple", "banana", false)]
    [DataRow("apple", "apple", true)]
    [DataRow("aab", "aaa", true)]
    public void Operator_StringContext_EvaluatesOp(string lhs, string rhs, bool expected)
    {
        var actual = EvaluateBinaryOp(CreateContext(), lhs, rhs) as VBBooleanValue;
        Assert.AreEqual(expected, actual?.Value);
    }

    [TestMethod]
    [DataRow(false, true, true)]
    [DataRow(true, false, false)]
    [DataRow(false, false, true)]
    public void Operator_BooleanContext_EvaluatesOp(bool lhs, bool rhs, bool expected)
    {
        var actual = EvaluateBinaryOp(CreateContext(), lhs, rhs) as VBBooleanValue;
        Assert.AreEqual(expected, actual?.Value);
    }

    [TestMethod]
    [DataRow(2, 1, true)]
    [DataRow(1, 2, false)]
    [DataRow(5, 5, true)]
    public void Operator_ByteContext_EvaluatesOp(object lhs, object rhs, bool expected)
    {
        var actual = EvaluateBinaryOp(CreateContext(), Convert.ToByte(lhs), Convert.ToByte(rhs)) as VBBooleanValue;
        Assert.AreEqual(expected, actual?.Value);
    }
}
