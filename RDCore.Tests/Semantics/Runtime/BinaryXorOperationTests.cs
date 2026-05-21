using RDCore.SDK.Model;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Expressions.Operators;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Model;
using RDCore.SDK.Semantics.Runtime.Abstract;
using RDCore.SDK.Semantics.Runtime.Operators;

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
        Assert.AreEqual(expected, actual?.ManagedValue);
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
        Assert.AreEqual(expected, actual?.ManagedValue);
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
        var name = "TestUDT";
        var symbol = new VBUserDefinedTypeMemberSymbol(
            ScopeKind.Module,
            TestUri.TestModuleUserDefinedTypeUri(name),
            name, Accessibility.Private,
            TestLocation.Range,
            TestLocation.Range,
            TestUri.WorkspaceRoot());
        var udt = new VBUserDefinedType(symbol, [], []);

        var lhs = VBNullValue.Null;
        var rhs = new LiteralExpression(TestLocation, new VBUserDefinedTypeValue(udt, symbol));

        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() =>
            EvaluateXor(CreateContext(), lhs, rhs));
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10 Let-coercion from 'Null'")]
    public void EvaluateXor_Null_LetCoercion_ResizableArray_TypeMismatch()
    {
        var lhs = VBNullValue.Null;
        var rhs = new LiteralExpression(TestLocation, VBResizableArrayValue.Empty);

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

    private VBTypedValue EvaluateXor(IVBExecutionContext context, object? lhs, object? rhs)
    {
        var lhsValue = WrapVBTypedValue(lhs, TestLocationLHS);
        var lhsExpression = WrapLiteralExpression(lhsValue, TestLocationLHS);

        var rhsValue = WrapVBTypedValue(rhs, TestLocationRHS);
        var rhsExpression = WrapLiteralExpression(rhsValue, TestLocationRHS);

        var expression = new VBBinaryOperatorExpression(GlobalSymbols.OperatorSymbols.BitwiseXOr, lhsExpression, rhsExpression, TestLocation);
        return Semantics.Evaluate(context, expression, lhsValue, rhsValue)!;
    }
}
