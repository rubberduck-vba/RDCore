using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Semantics.Runtime.Abstract;
using RDCore.SDK.Semantics.Runtime.Operators;

namespace RDCore.Tests.Semantics.Runtime;

[TestClass]
[TestCategory("MS-VBAL 5.6.9.5.2 Binary '<>' Operator")]
public class InequalityOperationTests : BinaryOperatorOperationTests
{
    protected override BinaryOperatorSymbol Symbol => GlobalSymbols.OperatorSymbols.CompareNeqOp;
    internal override IRuntimeSemantics Semantics => new BinaryNeqRelationalOperatorRuntimeSemantics();
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
    [DataRow(42, 42, false)]
    [DataRow(42, 43, true)]
    [DataRow(0, 0, false)]
    [DataRow(-1, 1, true)]
    public void Operator_IntegerContext_EvaluatesOp(int lhs, int rhs, bool expected)
    {
        var actual = EvaluateBinaryOp(CreateContext(), lhs, rhs) as VBBooleanValue;
        Assert.AreEqual(expected, actual?.Value);
    }

    [TestMethod]
    [DataRow(3.14, 3.14, false)]
    [DataRow(3.14, 2.71, true)]
    [DataRow(0.0, 0.0, false)]
    public void Operator_DoubleContext_EvaluatesOp(double lhs, double rhs, bool expected)
    {
        var actual = EvaluateBinaryOp(CreateContext(), lhs, rhs) as VBBooleanValue;
        Assert.AreEqual(expected, actual?.Value);
    }

    [TestMethod]
    [DataRow("hello", "hello", false)]
    [DataRow("hello", "HELLO", false)]  // Case-insensitive comparison
    [DataRow("hello", "world", true)]
    [DataRow("", "", false)]
    public void Operator_StringContext_EvaluatesOp(string lhs, string rhs, bool expected)
    {
        var actual = EvaluateBinaryOp(CreateContext(), lhs, rhs) as VBBooleanValue;
        Assert.AreEqual(expected, actual?.Value);
    }

    [TestMethod]
    [DataRow(true, true, false)]
    [DataRow(true, false, true)]
    [DataRow(false, false, false)]
    public void EvaluateInequality_BooleanOperands_CalculatesResult(bool lhs, bool rhs, bool expected)
    {
        var actual = EvaluateBinaryOp(CreateContext(), lhs, rhs) as VBBooleanValue;
        Assert.AreEqual(expected, actual?.Value);
    }

    [TestMethod]
    [DataRow(1, 1, false)]
    [DataRow(1, 2, true)]
    [DataRow(0, 0, false)]
    public void EvaluateInequality_ByteOperands_CalculatesResult(object lhs, object rhs, bool expected)
    {
        var actual = EvaluateBinaryOp(CreateContext(), Convert.ToByte(lhs), Convert.ToByte(rhs)) as VBBooleanValue;
        Assert.AreEqual(expected, actual?.Value);
    }
}
