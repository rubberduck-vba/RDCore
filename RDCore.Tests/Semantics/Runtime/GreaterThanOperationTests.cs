using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;
using RDCore.SDK.Semantics.Runtime.Abstract;
using RDCore.SDK.Semantics.Runtime.Operators;

namespace RDCore.Tests.Semantics.Runtime;

[TestClass]
[TestCategory("MS-VBAL 5.6.9.5.4 Binary '>' Operator")]
public class GreaterThanOperationTests : BinaryOperatorOperationTests
{
    protected override BinaryOperatorSymbol Symbol => GlobalSymbols.OperatorSymbols.CompareGtOp;

    internal override IRuntimeSemantics Semantics => new BinaryGtRelationalOperatorRuntimeSemantics();
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
    [DataRow(10, 10, false)]
    [DataRow(0, -5, true)]
    public void Operator_IntegerContext_EvaluatesOp(int lhs, int rhs, bool expected)
    {
        var actual = EvaluateGreaterThan(CreateContext(), lhs, rhs) as VBBooleanValue;
        Assert.AreEqual(expected, actual?.Value);
    }

    [TestMethod]
    [DataRow(2.5, 1.5, true)]
    [DataRow(1.5, 2.5, false)]
    [DataRow(1.5, 1.5, false)]
    [DataRow(-2.5, -3.5, true)]
    public void Operator_DoubleContext_EvaluatesOp(double lhs, double rhs, bool expected)
    {
        var actual = EvaluateGreaterThan(CreateContext(), lhs, rhs) as VBBooleanValue;
        Assert.AreEqual(expected, actual?.Value);
    }

    [TestMethod]
    [DataRow("banana", "apple", true)]
    [DataRow("apple", "banana", false)]
    [DataRow("apple", "apple", false)]
    [DataRow("aab", "aaa", true)]
    public void Operator_StringContext_EvaluatesOp(string lhs, string rhs, bool expected)
    {
        var actual = EvaluateGreaterThan(CreateContext(), lhs, rhs) as VBBooleanValue;
        Assert.AreEqual(expected, actual?.Value);
    }

    [TestMethod]
    [DataRow(false, true, true)] 
    [DataRow(true, false, false)]
    [DataRow(false, false, false)]
    public void EvaluateGreaterThan_BooleanOperands_CalculatesResult(bool lhs, bool rhs, bool expected)
    {
        var actual = EvaluateGreaterThan(CreateContext(), lhs, rhs) as VBBooleanValue;
        Assert.AreEqual(expected, actual?.Value);
    }

    [TestMethod]
    [DataRow(2, 1, true)]
    [DataRow(1, 2, false)]
    [DataRow(5, 5, false)]
    public void EvaluateGreaterThan_ByteOperands_CalculatesResult(object lhs, object rhs, bool expected)
    {
        var actual = EvaluateGreaterThan(CreateContext(), lhs, rhs) as VBBooleanValue;
        Assert.AreEqual(expected, actual?.Value);
    }

    [TestMethod]
    [DataRow("20", 10, true)]
    [DataRow("10", 20, false)]
    public void EvaluateGreaterThan_ImplicitCoercion(object lhs, object rhs, bool expected)
    {
        var result = EvaluateGreaterThan(CreateContext(), lhs, rhs);
        Assert.IsInstanceOfType<VBBooleanValue>(result);
        Assert.AreEqual(expected, ((VBBooleanValue)result).Value);
    }

    private VBTypedValue EvaluateGreaterThan(IVBExecutionContext context, object lhs, object rhs)
    {
        var lhsValue = WrapVBTypedValue(lhs, TestLocationLHS);
        var lhsExpression = WrapLiteralExpression(lhsValue, TestLocationLHS);

        var rhsValue = WrapVBTypedValue(rhs, TestLocationRHS);
        var rhsExpression = WrapLiteralExpression(rhs, TestLocationRHS);

        var expression = new VBBinaryOperatorExpression(GlobalSymbols.OperatorSymbols.CompareGtOp, TestLocation, lhsExpression, rhsExpression);
        return Semantics.Evaluate(context, expression, lhsValue, rhsValue)!;
    }
}
