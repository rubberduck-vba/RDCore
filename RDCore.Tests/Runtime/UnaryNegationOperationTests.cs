using RDCore.Parsing;
using RDCore.Parsing.Model.Symbols;
using RDCore.Parsing.Model.Types;
using RDCore.Parsing.Model.Types.Complex;
using RDCore.Parsing.Model.Values;
using RDCore.Runtime;
using RDCore.Runtime.Model;
using RDCore.Runtime.Model.Operators;
using RDCore.Runtime.Model.Operators.RuntimeSemantics;
using RDCore.Server;

namespace RDCore.Tests.Runtime;

[TestClass]
[TestCategory("MS-VBAL 5.6.9.3.1 Unary '-' Operator")]
public class UnaryNegationOperationTests : SymbolOperationTests
{
    internal override RuntimeSemantics Semantics => new UnaryNegationOperatorRuntimeSemantics();
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
        Assert.AreEqual(expected, actual?.NumericValue);
    }

    [TestMethod]
    [DataRow(3.5, -3.5)]
    [DataRow(-2.5, 2.5)]
    [DataRow(0.0, -0.0)]
    public void EvaluateUnaryNegation_DoubleOperand_CalculatesResult(object operand, double expected)
    {
        var actual = EvaluateUnaryNegation(CreateContext(), operand) as VBDoubleValue;
        Assert.AreEqual(expected, actual?.NumericValue);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10 Let-coercion from 'Null'")]
    public void EvaluateUnaryNegation_NullOperand_ResultIsNull()
    {
        var result = EvaluateUnaryNegation(CreateContext(), null);
        Assert.IsInstanceOfType<VBNullValue>(result);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10 Let-coercion from 'Null'")]
    public void EvaluateUnaryNegation_Null_LetCoercion_UDT_TypeMismatch()
    {
        var udt = new VBUserDefinedType("Test", new VBUserDefinedTypeMember(new Uri("file://TestProject/TestModule/TestUDT"), "TestUDT", TestLocation.Range, TestLocation.Range, new Uri("file://TestProject")));

        var operand = new LiteralExpression(TestLocation, new VBUserDefinedTypeValue(udt));

        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() =>
            EvaluateUnaryNegation(CreateContext(), operand));
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10 Let-coercion from 'Null'")]
    public void EvaluateUnaryNegation_Null_LetCoercion_ResizableArray_TypeMismatch()
    {
        var operand = new LiteralExpression(TestLocation, new VBResizableArrayValue(0, 0, VBIntegerType.TypeInfo));

        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() =>
            EvaluateUnaryNegation(CreateContext(), operand));
    }

    [TestMethod]
    [DataRow("42", -42.0)]  // String coerced to numeric
    [DataRow("-10", 10.0)]
    public void EvaluateUnaryNegation_ImplicitCoercion(object operand, double expected)
    {
        var result = EvaluateUnaryNegation(CreateContext(), operand);
        Assert.IsInstanceOfType<VBNumericTypedValue>(result);
        Assert.AreEqual(expected, ((VBNumericTypedValue)result).NumericValue, 0.0001);
    }

    [TestCategory("Diagnostics.VBRuntimeError.TypeMismatch")]
    [TestMethod]
    public void EvaluateUnaryNegation_VBErrorValue_TypeMismatch()
    {
        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() =>
            EvaluateUnaryNegation(CreateContext(), "VBErrorValue"));
    }

    private VBTypedValue EvaluateUnaryNegation(VBExecutionContext context, object? operand)
    {
        var operandValue = WrapLiteralExpression(operand, TestLocationLHS);
        var expression = new VBUnaryOperatorExpression(GlobalSymbols.UnaryNegation, TestLocation, operandValue);

        return Semantics.Evaluate(context, expression, operandValue.ResultValue)!;
    }
}
