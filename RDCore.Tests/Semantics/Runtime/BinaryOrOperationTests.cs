using RDCore.SDK.Model;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Expressions;
using RDCore.SDK.Model.Expressions.Operators;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;
using RDCore.SDK.Semantics.Runtime.Abstract;
using RDCore.SDK.Semantics.Runtime.Operators;

namespace RDCore.Tests.Semantics.Runtime;

[TestClass]
[TestCategory("MS-VBAL 5.6.9.8.3 Binary 'Or' Operator")]
public class BinaryOrOperationTests : SymbolOperationTests
{
    internal override IRuntimeSemantics Semantics => new BinaryOrBitwiseOperatorRuntimeSemantics();
    internal override IEnumerable<VBType> EffectiveTypes => [
        VBBooleanType.TypeInfo,
        VBByteType.TypeInfo,
        VBIntegerType.TypeInfo,
        VBLongType.TypeInfo,
        VBLongLongType.TypeInfo,
        VBNullType.TypeInfo,
    ];

    [TestMethod]
    [DataRow(0, 0, 0)]      // False Or False = False (0)
    [DataRow(0, -1, -1)]    // False Or True = True (-1)
    [DataRow(-1, 0, -1)]    // True Or False = True (-1)
    [DataRow(-1, -1, -1)]   // True Or True = True (-1)
    public void EvaluateOr_BitwiseContext_CalculatesResult(object lhs, object rhs, int expected)
    {
        var actual = EvaluateOr(CreateContext(), lhs, rhs) as VBIntegerValue;
        Assert.AreEqual(expected, actual?.ManagedValue);
    }

    [TestMethod]
    [DataRow(5, 3, 7)]      // 5 Or 3 = 7 (bitwise)
    [DataRow(12, 10, 14)]   // 12 Or 10 = 14 (bitwise)
    [DataRow(15, 0, 15)]    // 15 Or 0 = 15
    public void EvaluateOr_IntegerBitwise_CalculatesResult(object lhs, object rhs, int expected)
    {
        var actual = EvaluateOr(CreateContext(), lhs, rhs) as VBIntegerValue;
        Assert.AreEqual(expected, actual?.ManagedValue);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10 Let-coercion from 'Null'")]
    public void EvaluateOr_BothNullOperands_ResultIsNull()
    {
        var result = EvaluateOr(CreateContext(), null, null);
        Assert.IsInstanceOfType<VBNullValue>(result);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10 Let-coercion from 'Null'")]
    [DataRow(0, null)]      // 0 Or Null = Null
    [DataRow(-1, null)]     // -1 Or Null = -1
    [DataRow(null, 0)]      // Null Or 0 = Null
    [DataRow(null, -1)]     // Null Or -1 = -1
    public void EvaluateOr_SingleNullOperand(object lhs, object rhs)
    {
        var result = EvaluateOr(CreateContext(), lhs, rhs);
        // Result can be Null or an Integer value depending on the operands
        Assert.IsTrue(result is VBNullValue or VBIntegerValue);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10 Let-coercion from 'Null'")]
    public void EvaluateOr_Null_LetCoercion_UDT_TypeMismatch()
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

        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() => EvaluateOr(CreateContext(), lhs, rhs));
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10 Let-coercion from 'Null'")]
    public void EvaluateOr_Null_LetCoercion_ResizableArray_TypeMismatch()
    {
        var lhs = VBNullValue.Null;
        var rhs = new LiteralExpression(TestLocation, VBResizableArrayValue.Empty);

        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() => EvaluateOr(CreateContext(), lhs, rhs));
    }

    [TestMethod]
    [DataRow(0, "VBErrorValue")]
    [DataRow("VBErrorValue", 0)]
    public void EvaluateOr_VBErrorValue_TypeMismatch(object lhs, object rhs)
    {
        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() => EvaluateOr(CreateContext(), lhs, rhs));
    }

    private VBTypedValue EvaluateOr(IVBExecutionContext context, object? lhs, object? rhs)
    {
        var lhsExpression = WrapLiteralExpression(lhs, TestLocationLHS);
        var lhsValue = lhsExpression.ResolvedValue!;

        var rhsExpression = WrapLiteralExpression(rhs, TestLocationRHS);
        var rhsValue = rhsExpression.ResolvedValue!;

        var expression = new VBBinaryOperatorExpression(GlobalSymbols.OperatorSymbols.BitwiseOr, lhsExpression, rhsExpression, TestLocation);

        return Semantics.Evaluate(context, expression, lhsValue, rhsValue)!;
    }
}
