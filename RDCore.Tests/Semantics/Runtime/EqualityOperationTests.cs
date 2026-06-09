using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Semantics.Runtime.Abstract;
using RDCore.SDK.Semantics.Runtime.Operators;

namespace RDCore.Tests.Semantics.Runtime;

[TestClass]
[TestCategory("MS-VBAL 5.6.9.5.1 Binary '=' Operator")]
public class EqualityOperationTests : BinaryOperatorOperationTests
{
    protected override BinaryOperatorSymbol Symbol => GlobalSymbols.OperatorSymbols.CompareEqOp;
    internal override IRuntimeSemantics Semantics => new BinaryEqRelationalOperatorRuntimeSemantics();
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
    [DataRow(42, 42, true)]
    [DataRow(42, 43, false)]
    [DataRow(0, 0, true)]
    [DataRow(-1, -1, true)]
    public void EvaluateEquality_IntegerOperands_CalculatesResult(int lhs, int rhs, bool expected)
    {
        var actual = EvaluateBinaryOp(CreateContext(), lhs, rhs) as VBBooleanValue;
        Assert.AreEqual(expected, actual?.Value);
    }

    [TestMethod]
    [DataRow(3.14, 3.14, true)]
    [DataRow(3.14, 2.71, false)]
    [DataRow(0.0, 0.0, true)]
    public void Operator_DoubleContext_EvaluatesOp(double lhs, double rhs, bool expected)
    {
        var actual = EvaluateBinaryOp(CreateContext(), lhs, rhs) as VBBooleanValue;
        Assert.AreEqual(expected, actual?.Value);
    }

    [TestMethod]
    [DataRow("hello", "hello", true)]
    [DataRow("hello", "HELLO", true)]  // Case-insensitive comparison
    [DataRow("hello", "world", false)]
    [DataRow("", "", true)]
    public void Operator_StringContext_EvaluatesOp(string lhs, string rhs, bool expected)
    {
        var actual = EvaluateBinaryOp(CreateContext(), lhs, rhs) as VBBooleanValue;
        Assert.AreEqual(expected, actual?.Value);
    }

    [TestMethod]
    [DataRow(true, true, true)]
    [DataRow(true, false, false)]
    [DataRow(false, false, true)]
    public void Operator_BooleanContext_EvaluatesOp(bool lhs, bool rhs, bool expected)
    {
        var actual = EvaluateBinaryOp(CreateContext(), lhs, rhs) as VBBooleanValue;
        Assert.AreEqual(expected, actual?.Value);
    }
}
