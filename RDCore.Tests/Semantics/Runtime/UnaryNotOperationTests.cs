using RDCore.SDK.Model;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Expressions.Operators;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Types.Intrinsic;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Model;
using RDCore.SDK.Semantics.Runtime.Abstract;
using RDCore.SDK.Semantics.Runtime.Operators;

namespace RDCore.Tests.Semantics.Runtime;

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
        var udt = new VBUserDefinedType("Test", new VBUserDefinedTypeMemberSymbol(ScopeKind.Module, new Uri("file://TestProject/TestModule/TestUDT"), "UDT", Accessibility.Public, TestLocation.Range, TestLocation.Range, new Uri("file://TestProject")));
        var operand = new LiteralExpression(TestLocation, new VBUserDefinedTypeValue(udt));

        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() => EvaluateUnaryNot(CreateContext(), operand));
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10 Let-coercion from 'Null'")]
    public void EvaluateUnaryNot_Null_LetCoercion_ResizableArray_TypeMismatch()
    {
        var operand = new LiteralExpression(TestLocation, VBResizableArrayValue.Empty);

        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() => EvaluateUnaryNot(CreateContext(), operand));
    }

    [TestCategory("Diagnostics.VBRuntimeError.TypeMismatch")]
    [TestMethod]
    public void EvaluateUnaryNot_VBErrorValue_TypeMismatch()
    {
        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() => EvaluateUnaryNot(CreateContext(), "VBErrorValue"));
    }

    private VBTypedValue EvaluateUnaryNot(IVBExecutionContext context, object operand)
    {
        var operandValue = WrapVBTypedValue(operand, TestLocationLHS);
        var operandExpression = WrapLiteralExpression(operandValue, TestLocationLHS);

        var expression = new VBUnaryOperatorExpression(GlobalSymbols.BitwiseNot, TestLocation, operandExpression);
        return Semantics.Evaluate(context, expression, operandValue)!;
    }
}
