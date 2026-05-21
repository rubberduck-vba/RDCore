using RDCore.SDK.Model.Expressions.Operators;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;
using RDCore.SDK.Semantics.Runtime.Abstract;
using RDCore.SDK.Semantics.Runtime.Operators;

namespace RDCore.Tests.Semantics.Runtime;

[TestClass]
[TestCategory("MS-VBAL 5.6.9.8.1 Unary 'Not' Operator")]
public class UnaryNotOperationTests : SymbolOperationTests
{
    internal override IRuntimeSemantics Semantics => new UnaryNotOperatorRuntimeSemantics();
    internal override IEnumerable<VBType> EffectiveTypes => [
        VBBooleanType.TypeInfo,
        VBByteType.TypeInfo,
        VBIntegerType.TypeInfo,
        VBLongType.TypeInfo,
        VBLongLongType.TypeInfo,
        VBNullType.TypeInfo,
    ];

    [TestMethod]
    [DataRow(0, -1)]
    [DataRow(-1, 0)]
    [DataRow(5, -6)]
    [DataRow(-5, 4)]
    public void EvaluateUnaryNot_IntegerOperand_CalculatesResult(object operand, int expected)
    {
        var actual = EvaluateUnaryNot(CreateContext(), operand) as VBIntegerValue;
        Assert.AreEqual(expected, actual?.ManagedValue);
    }

    [TestMethod]
    [DataRow(true, 0)]
    [DataRow(false, -1)]
    public void EvaluateUnaryNot_BooleanOperand_CalculatesResult(object operand, object expected)
    {
        var actual = EvaluateUnaryNot(CreateContext(), operand) as VBIntegerValue;
        Assert.AreEqual(Convert.ToInt16(expected), actual?.Value);
    }

    private VBTypedValue EvaluateUnaryNot(IVBExecutionContext context, object operand)
    {
        var operandValue = WrapVBTypedValue(operand, TestLocationLHS);
        var operandExpression = WrapLiteralExpression(operandValue, TestLocationLHS);

        var expression = new VBUnaryOperatorExpression(GlobalSymbols.OperatorSymbols.BitwiseNot, TestLocation, operandExpression);
        return Semantics.Evaluate(context, expression, operandValue)!;
    }
}
