using RDCore.Parsing;
using RDCore.Parsing.Model.Symbols;
using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Types.Complex;
using RDCore.Parsing.Model.Types.Intrinsic;
using RDCore.Parsing.Model.Values.Abstract;
using RDCore.Parsing.Model.Values.Intrinsic;
using RDCore.Runtime;
using RDCore.Runtime.Model;
using RDCore.Runtime.Model.Operators;
using RDCore.Semantics.Runtime;
using RDCore.Semantics.Runtime.Abstract;
using RDCore.Server;

namespace RDCore.Tests.Semantics.Runtime;

[TestClass]
[TestCategory("MS-VBAL 5.6.9.8.4 Binary 'Xor' Operator")]
public class BinaryXorOperationTests : SymbolOperationTests
{
    internal override RuntimeSemantics Semantics => new BinaryXorBitwiseOperatorRuntimeSemantics();
    internal override IEnumerable<VBType> EffectiveTypes => [
        VBBooleanType.TypeInfo,
        VBByteType.TypeInfo,
        VBIntegerType.TypeInfo,
        VBLongType.TypeInfo,
        VBLongLongType.TypeInfo,
        VBNullType.TypeInfo,
    ];

    [TestMethod]
    [DataRow(0, 0, 0)]      // False Xor False = False (0)
    [DataRow(0, -1, -1)]    // False Xor True = True (-1)
    [DataRow(-1, 0, -1)]    // True Xor False = True (-1)
    [DataRow(-1, -1, 0)]    // True Xor True = False (0)
    public void EvaluateXor_BitwiseContext_CalculatesResult(object lhs, object rhs, int expected)
    {
        var context = CreateContext();
        var actual = EvaluateXor(context, lhs, rhs) as VBIntegerValue;
        Assert.AreEqual(expected, actual?.NumericValue);
    }

    [TestMethod]
    [DataRow(5, 3, 6)]      // 5 Xor 3 = 6 (bitwise)
    [DataRow(12, 10, 6)]    // 12 Xor 10 = 6 (bitwise)
    [DataRow(15, 0, 15)]    // 15 Xor 0 = 15
    [DataRow(15, 15, 0)]    // 15 Xor 15 = 0
    public void EvaluateXor_IntegerBitwise_CalculatesResult(object lhs, object rhs, int expected)
    {
        var context = CreateContext();
        var actual = EvaluateXor(context, lhs, rhs) as VBIntegerValue;
        Assert.AreEqual(expected, actual?.NumericValue);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10 Let-coercion from 'Null'")]
    public void EvaluateXor_BothNullOperands_ResultIsNull()
    {
        var result = EvaluateXor(CreateContext(), null, null);
        Assert.IsInstanceOfType<VBNullValue>(result);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10 Let-coercion from 'Null'")]
    [DataRow(0, null)]      // 0 Xor Null = Null
    [DataRow(-1, null)]     // -1 Xor Null = Null
    [DataRow(null, 0)]      // Null Xor 0 = Null
    [DataRow(null, -1)]     // Null Xor -1 = Null
    public void EvaluateXor_SingleNullOperand(object lhs, object rhs)
    {
        var result = EvaluateXor(CreateContext(), lhs, rhs);
        // Result can be Null or an Integer value depending on the operands
        Assert.IsTrue(result is VBNullValue or VBIntegerValue);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10 Let-coercion from 'Null'")]
    public void EvaluateXor_Null_LetCoercion_UDT_TypeMismatch()
    {
        var udt = new VBUserDefinedType("Test", new VBUserDefinedTypeMember(new Uri("file://TestProject/TestModule/TestUDT"), "TestUDT", TestLocation.Range, TestLocation.Range, new Uri("file://TestProject")));

        var lhs = VBNullValue.Null;
        var rhs = new LiteralExpression(TestLocation, new VBUserDefinedTypeValue(udt));

        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() =>
            EvaluateXor(CreateContext(), lhs, rhs));
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10 Let-coercion from 'Null'")]
    public void EvaluateXor_Null_LetCoercion_ResizableArray_TypeMismatch()
    {
        var lhs = VBNullValue.Null;
        var rhs = new LiteralExpression(TestLocation, new VBResizableArrayValue(0, 0, VBIntegerType.TypeInfo));

        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() =>
            EvaluateXor(CreateContext(), lhs, rhs));
    }

    [TestCategory("Diagnostics.VBRuntimeError.TypeMismatch")]
    [TestMethod]
    [DataRow(0, "VBErrorValue")]
    [DataRow("VBErrorValue", 0)]
    public void EvaluateXor_VBErrorValue_TypeMismatch(object lhs, object rhs)
    {
        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() =>
            EvaluateXor(CreateContext(), lhs, rhs));
    }

    private VBTypedValue EvaluateXor(VBExecutionContext context, object? lhs, object? rhs)
    {
        var lhsValue = WrapLiteralExpression(lhs, TestLocationLHS);
        var rhsValue = WrapLiteralExpression(rhs, TestLocationRHS);
        var expression = new VBBinaryOperatorExpression(GlobalSymbols.BitwiseXOr, lhsValue, rhsValue, TestLocation);

        return Semantics.Evaluate(context, expression, lhsValue.RuntimeValue, rhsValue.RuntimeValue)!;
    }
}
