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
[TestCategory("MS-VBAL 5.6.9.8.1 Unary 'Not' Operator")]
public class UnaryNotOperationTests : SymbolOperationTests
{
    internal override RuntimeSemantics Semantics => new UnaryNotOperatorRuntimeSemantics();
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
        Assert.AreEqual(expected, actual?.NumericValue);
    }

    [TestMethod]
    [DataRow(true, 0)]
    [DataRow(false, -1)]
    public void EvaluateUnaryNot_BooleanOperand_CalculatesResult(object operand, object expected)
    {
        var actual = EvaluateUnaryNot(CreateContext(), operand) as VBIntegerValue;
        Assert.AreEqual(Convert.ToInt16(expected), actual?.Value);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10 Let-coercion from 'Null'")]
    public void EvaluateUnaryNot_NullOperand_ResultIsNull()
    {
        var result = EvaluateUnaryNot(CreateContext(), null);
        Assert.IsInstanceOfType<VBNullValue>(result);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10 Let-coercion from 'Null'")]
    public void EvaluateUnaryNot_Null_LetCoercion_UDT_TypeMismatch()
    {
        var udt = new VBUserDefinedType("Test", new VBUserDefinedTypeMember(new Uri("file://TestProject/TestModule/TestUDT"), "TestUDT", TestLocation.Range, TestLocation.Range, new Uri("file://TestProject")));
        var operand = new LiteralExpression(TestLocation, new VBUserDefinedTypeValue(udt));

        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() =>
            EvaluateUnaryNot(CreateContext(), operand));
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10 Let-coercion from 'Null'")]
    public void EvaluateUnaryNot_Null_LetCoercion_ResizableArray_TypeMismatch()
    {
        var operand = new LiteralExpression(TestLocation, new VBResizableArrayValue(0, 0, VBIntegerType.TypeInfo));

        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() =>
            EvaluateUnaryNot(CreateContext(), operand));
    }

    [TestCategory("Diagnostics.VBRuntimeError.TypeMismatch")]
    [TestMethod]
    public void EvaluateUnaryNot_VBErrorValue_TypeMismatch()
    {
        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() =>
            EvaluateUnaryNot(CreateContext(), "VBErrorValue"));
    }

    private VBTypedValue EvaluateUnaryNot(VBExecutionContext context, object operand)
    {
        var operandValue = WrapLiteralExpression(operand, TestLocationLHS);
        var expression = new VBUnaryOperatorExpression(GlobalSymbols.BitwiseNot, TestLocation, operandValue);

        return Semantics.Evaluate(context, expression, operandValue.ResultValue)!;
    }
}
