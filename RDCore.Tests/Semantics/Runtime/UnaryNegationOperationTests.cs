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
[TestCategory("MS-VBAL 5.6.9.3.1 Unary '-' Operator")]
public class UnaryNegationOperationTests : SymbolOperationTests
{
    internal override IRuntimeSemantics Semantics => new UnaryNegationOperatorRuntimeSemantics();
    internal override IEnumerable<VBType> EffectiveTypes => [
        VBByteType.TypeInfo,
        VBIntegerType.TypeInfo,
        VBLongType.TypeInfo,
        VBLongLongType.TypeInfo,
        VBSingleType.TypeInfo,
        VBDoubleType.TypeInfo,
        VBCurrencyType.TypeInfo,
        VBDecimalType.TypeInfo,
        VBDateType.TypeInfo,
        VBNullType.TypeInfo,
    ];

    [TestMethod]
    [DataRow(5, -5)]
    [DataRow(-5, 5)]
    [DataRow(0, 0)]
    [DataRow(32767, -32767)]
    public void EvaluateUnaryNegation_IntegerOperand_CalculatesResult(object operand, int expected)
    {
        var actual = EvaluateUnaryNegation(CreateContext(), operand) as VBIntegerValue;
        Assert.AreEqual(expected, actual?.ManagedValue);
    }

    [TestMethod]
    [DataRow(3.5, -3.5)]
    [DataRow(-2.5, 2.5)]
    [DataRow(0.0, -0.0)]
    public void EvaluateUnaryNegation_DoubleOperand_CalculatesResult(object operand, double expected)
    {
        var actual = EvaluateUnaryNegation(CreateContext(), operand) as VBDoubleValue;
        Assert.AreEqual(expected, actual?.ManagedValue);
    }

    [TestMethod]
    [DataRow("42", -42.0)]  // String coerced to numeric
    [DataRow("-10", 10.0)]
    public void EvaluateUnaryNegation_ImplicitCoercion(object operand, double expected)
    {
        var result = EvaluateUnaryNegation(CreateContext(), operand);
        Assert.IsInstanceOfType<VBNumericTypedValue>(result);
        Assert.AreEqual(expected, ((VBNumericTypedValue)result).ManagedValue, 0.0001);
    }

    private VBTypedValue EvaluateUnaryNegation(IVBExecutionContext context, object? operand)
    {
        var operandValue = WrapVBTypedValue(operand, TestLocationLHS);
        var operandExpression = WrapLiteralExpression(operandValue, TestLocationLHS);

        var expression = new VBUnaryOperatorExpression(GlobalSymbols.OperatorSymbols.UnaryNegation, TestLocation, operandExpression);
        return Semantics.Evaluate(context, expression, operandValue)!;
    }
}
