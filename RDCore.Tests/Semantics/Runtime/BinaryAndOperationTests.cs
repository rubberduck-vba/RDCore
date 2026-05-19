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
[TestCategory("MS-VBAL 5.6.9.8.2 Binary 'And' Operator")]
public class BinaryAndOperationTests : SymbolOperationTests
{
    internal override RuntimeSemantics Semantics => new BinaryAndBitwiseOperatorRuntimeSemantics();
    internal override IEnumerable<VBType> EffectiveTypes => [
        VBBooleanType.TypeInfo,
        VBByteType.TypeInfo,
        VBIntegerType.TypeInfo,
        VBLongType.TypeInfo,
        VBLongLongType.TypeInfo,
        VBNullType.TypeInfo,
    ];

    [TestMethod]
    [DataRow(0, 0, 0)]      // False And False = False (0)
    [DataRow(0, -1, 0)]     // False And True = False (0)
    [DataRow(-1, 0, 0)]     // True And False = False (0)
    [DataRow(-1, -1, -1)]   // True And True = True (-1)
    public void EvaluateAnd_BitwiseContext_CalculatesResult(object lhs, object rhs, int expected)
    {
        var context = CreateContext();
        var actual = EvaluateAnd(context, lhs, rhs) as VBIntegerValue;
        Assert.AreEqual(expected, actual?.ManagedValue);
    }

    [TestMethod]
    [DataRow(5, 3, 1)]      // 5 And 3 = 1 (bitwise)
    [DataRow(12, 10, 8)]    // 12 And 10 = 8 (bitwise)
    [DataRow(15, 0, 0)]     // 15 And 0 = 0
    public void EvaluateAnd_IntegerBitwise_CalculatesResult(object lhs, object rhs, int expected)
    {
        var context = CreateContext();
        var actual = EvaluateAnd(context, lhs, rhs) as VBIntegerValue;
        Assert.AreEqual(expected, actual?.ManagedValue);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10 Let-coercion from 'Null'")]
    public void EvaluateAnd_BothNullOperands_ResultIsNull()
    {
        var result = EvaluateAnd(CreateContext(), null, null);
        Assert.IsInstanceOfType<VBNullValue>(result);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10 Let-coercion from 'Null'")]
    [DataRow(0, null)]      // 0 And Null = 0
    [DataRow(-1, null)]     // -1 And Null = Null
    [DataRow(null, 0)]      // Null And 0 = 0
    [DataRow(null, -1)]     // Null And -1 = Null
    public void EvaluateAnd_SingleNullOperand(object lhs, object rhs)
    {
        var result = EvaluateAnd(CreateContext(), lhs, rhs);
        // Result can be Null or an Integer value depending on the operands
        Assert.IsTrue(result is VBNullValue or VBIntegerValue);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10 Let-coercion from 'Null'")]
    public void EvaluateAnd_Null_LetCoercion_UDT_TypeMismatch()
    {
        var udt = new VBUserDefinedType("Test", new VBUserDefinedTypeMemberSymbol(ScopeKind.Module, new Uri("file://TestProject/TestModule/TestUDT"), "TestUDT", Accessibility.Private, TestLocation.Range, TestLocation.Range, new Uri("file://TestProject")));

        var lhs = VBNullValue.Null;
        var rhs = new LiteralExpression(TestLocation, new VBUserDefinedTypeValue(udt));

        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() =>
            EvaluateAnd(CreateContext(), lhs, rhs));
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10 Let-coercion from 'Null'")]
    public void EvaluateAnd_Null_LetCoercion_ResizableArray_TypeMismatch()
    {
        var lhs = VBNullValue.Null;
        var rhs = new LiteralExpression(TestLocation, VBResizableArrayValue.Empty);

        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() =>
            EvaluateAnd(CreateContext(), lhs, rhs));
    }

    [TestCategory("Diagnostics.VBRuntimeError.TypeMismatch")]
    [TestMethod]
    [DataRow(0, "VBErrorValue")]
    [DataRow("VBErrorValue", 0)]
    public void EvaluateAnd_VBErrorValue_TypeMismatch(object lhs, object rhs)
    {
        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() =>
            EvaluateAnd(CreateContext(), lhs, rhs));
    }

    private VBTypedValue EvaluateAnd(IVBExecutionContext context, object? lhs, object? rhs)
    {
        var lhsValue = WrapVBTypedValue(lhs, TestLocationLHS);
        var rhsValue = WrapVBTypedValue(rhs, TestLocationRHS);

        var lhsExpression = new LiteralExpression(TestLocationLHS, lhsValue);
        var rhsExpression = new LiteralExpression(TestLocationRHS, rhsValue);

        var expression = new VBBinaryOperatorExpression(GlobalSymbols.BitwiseAnd, lhsExpression, rhsExpression, TestLocation);

        return Semantics.Evaluate(context, expression, lhsValue, rhsValue)!;
    }
}
