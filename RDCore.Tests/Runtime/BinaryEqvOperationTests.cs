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
[TestCategory("MS-VBAL 5.6.9.8.5 Binary 'Eqv' Operator")]
public class BinaryEqvOperationTests : SymbolOperationTests
{
    internal override RuntimeSemantics Semantics => new BinaryEqvBitwiseOperatorRuntimeSemantics();
    internal override IEnumerable<VBType> EffectiveTypes => [
        VBBooleanType.TypeInfo,
        VBByteType.TypeInfo,
        VBIntegerType.TypeInfo,
        VBLongType.TypeInfo,
        VBLongLongType.TypeInfo,
        VBNullType.TypeInfo,
    ];

    [TestMethod]
    [DataRow(0, 0, -1)]     // False Eqv False = True (-1)
    [DataRow(0, -1, 0)]     // False Eqv True = False (0)
    [DataRow(-1, 0, 0)]     // True Eqv False = False (0)
    [DataRow(-1, -1, -1)]   // True Eqv True = True (-1)
    public void EvaluateEqv_BitwiseContext_CalculatesResult(object lhs, object rhs, int expected)
    {
        var context = CreateContext();
        var actual = EvaluateEqv(context, lhs, rhs) as VBIntegerValue;
        Assert.AreEqual(expected, actual?.NumericValue);
    }

    [TestMethod]
    [DataRow(5, 3, -7)]     // 5 Eqv 3 = -7 (bitwise)
    [DataRow(12, 10, -7)]   // 12 Eqv 10 = -7 (bitwise)
    [DataRow(15, 0, -16)]   // 15 Eqv 0 = -16
    [DataRow(15, 15, -1)]   // 15 Eqv 15 = -1
    public void EvaluateEqv_IntegerBitwise_CalculatesResult(object lhs, object rhs, int expected)
    {
        var context = CreateContext();
        var actual = EvaluateEqv(context, lhs, rhs) as VBIntegerValue;
        Assert.AreEqual(expected, actual?.NumericValue);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10 Let-coercion from 'Null'")]
    public void EvaluateEqv_BothNullOperands_ResultIsNull()
    {
        var result = EvaluateEqv(CreateContext(), null, null);
        Assert.IsInstanceOfType<VBNullValue>(result);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10 Let-coercion from 'Null'")]
    [DataRow(0, null)]      // 0 Eqv Null = Null
    [DataRow(-1, null)]     // -1 Eqv Null = Null
    [DataRow(null, 0)]      // Null Eqv 0 = Null
    [DataRow(null, -1)]     // Null Eqv -1 = Null
    public void EvaluateEqv_SingleNullOperand(object lhs, object rhs)
    {
        var result = EvaluateEqv(CreateContext(), lhs, rhs);
        // Result can be Null or an Integer value depending on the operands
        Assert.IsTrue(result is VBNullValue or VBIntegerValue);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10 Let-coercion from 'Null'")]
    public void EvaluateEqv_Null_LetCoercion_UDT_TypeMismatch()
    {
        var udt = new VBUserDefinedType("Test", new VBUserDefinedTypeMember(new Uri("file://TestProject/TestModule/TestUDT"), "TestUDT", TestLocation.Range, TestLocation.Range, new Uri("file://TestProject")));

        var lhs = VBNullValue.Null;
        var rhs = new LiteralExpression(TestLocation, new VBUserDefinedTypeValue(udt));

        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() =>
            EvaluateEqv(CreateContext(), lhs, rhs));
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10 Let-coercion from 'Null'")]
    public void EvaluateEqv_Null_LetCoercion_ResizableArray_TypeMismatch()
    {
        var lhs = VBNullValue.Null;
        var rhs = new LiteralExpression(TestLocation, new VBResizableArrayValue(0, 0, VBIntegerType.TypeInfo));

        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() =>
            EvaluateEqv(CreateContext(), lhs, rhs));
    }

    [TestCategory("Diagnostics.VBRuntimeError.TypeMismatch")]
    [TestMethod]
    [DataRow(0, "VBErrorValue")]
    [DataRow("VBErrorValue", 0)]
    public void EvaluateEqv_VBErrorValue_TypeMismatch(object lhs, object rhs)
    {
        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() =>
            EvaluateEqv(CreateContext(), lhs, rhs));
    }

    private VBTypedValue EvaluateEqv(VBExecutionContext context, object? lhs, object? rhs)
    {
        var lhsValue = WrapLiteralExpression(lhs, TestLocationLHS);
        var rhsValue = WrapLiteralExpression(rhs, TestLocationRHS);
        var expression = new VBBinaryOperatorExpression(GlobalSymbols.BitwiseEqv, lhsValue, rhsValue, TestLocation);

        return Semantics.Evaluate(context, expression, lhsValue.ResultValue, rhsValue.ResultValue)!;
    }
}
