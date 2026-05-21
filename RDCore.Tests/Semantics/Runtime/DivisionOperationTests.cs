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
[TestCategory("MS-VBAL 5.6.9.3.5 Binary '/' Operator")]
public class DivisionOperationTests : SymbolOperationTests
{
    internal override IRuntimeSemantics Semantics => new BinaryDivisionOperatorRuntimeSemantics();
    internal override IEnumerable<VBType> EffectiveTypes => [
        VBSingleType.TypeInfo,
        VBDoubleType.TypeInfo,
        VBDecimalType.TypeInfo,
        VBNullType.TypeInfo,
    ];

    [TestMethod]
    [DataRow(2, 2, 1)]
    [DataRow(-2, 2, -1)]
    [DataRow(-2, -2, 1)]
    [DataRow(2, -2, -1)]
    [DataRow(12, 2, 6)]
    [DataRow(5, 2, 2.5)]
    public void EvaluateDivision_HappyPath_CalculatesResult(object lhs, object rhs, object expected)
    {
        var actual = EvaluateDivision(CreateContext(), lhs, rhs) as INumericValue;
        Assert.AreEqual(Convert.ToDouble(expected), actual?.ManagedValue);
    }

    [TestMethod]
    [TestCategory("Diagnostics.VBRuntimeError.DivisionByZero")]
    [DataRow(1, 0, "VBR00011")]
    [DataRow(-1, 0, "VBR00011")]
    public void EvaluateDivision_DivisionByZero(object lhs, object rhs, object expected)
    {
        Assert.Throws<VBRuntimeErrorDivisionByZeroException>(() => EvaluateDivision(CreateContext(), lhs, rhs));
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10: Let-coercion from 'Null'")]
    [DataRow(null, null)]
    [DataRow(null, 5)]
    [DataRow(null, 1.23d)]
    [DataRow(null, "#1-1-2026#")]
    [DataRow(5, null)]
    [DataRow("Empty", null)]
    [DataRow(1.23d, null)]
    [DataRow("#2026-12-31#", null)]
    public void EvaluateDivision_NullOperand_ResultIsNull(object lhs, object rhs)
    {
        // note: coercing the result to any other type would throw.
        var result = EvaluateDivision(CreateContext(), lhs, rhs);
        Assert.IsInstanceOfType<VBNullValue>(result);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10: Let-coercion from 'Null'")]
    public void EvaluateDivision_Null_LetCoercion_UDT_TypeMismatch()
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
        var value = new VBUserDefinedTypeValue(udt, symbol);

        var lhs = VBNullValue.Null;
        var rhs = new LiteralExpression(TestLocation, value);

        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() =>
            EvaluateDivision(CreateContext(), lhs, rhs));
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10: Let-coercion from 'Null'")]
    public void EvaluateDivision_Null_LetCoercion_ResizableArray_TypeMismatch()
    {
        var lhs = VBNullValue.Null;
        var rhs = new LiteralExpression(TestLocation, VBResizableArrayValue.Empty);

        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() => EvaluateDivision(CreateContext(), lhs, rhs));
    }

    [TestMethod]
    [DataRow(-1, "DateTime.Now")]
    [DataRow("DateTime.Now", 1)]
    [DataRow("DateTime.Now", "DateTime.Now")]
    public void EvaluateDivision_DateTime_ReturnsDouble(object lhs, object rhs)
    {
        var result = EvaluateDivision(CreateContext(), lhs, rhs);
        Assert.IsInstanceOfType<VBDoubleValue>(result);
    }

    [TestMethod]
    [DataRow("1.5", 1, 1.5d)]
    [DataRow(-32767, 0.5d, -65534.0d)]
    public void EvaluateDivision_NumericCoercion(object lhs, object rhs, object expected)
    {
        var context = CreateContext();
        var result = EvaluateDivision(context, lhs, rhs);
        if (expected is not string)
        {
            Assert.AreEqual(Convert.ToDouble(expected), ((INumericValue)result).ManagedValue, 0.0001);
        }
    }

    [TestMethod]
    [TestCategory("Diagnostics.VBRuntimeError.Overflow")]
    [DataRow(32767, 2)]
    [DataRow(-32768, 2)]
    [DataRow(0, 0)]
    public void EvaluateDivision_Overflow(object lhs, object rhs)
    {
        Assert.Throws<VBRuntimeErrorOverflowException>(() => EvaluateDivision(CreateContext(), lhs, rhs));
    }

    [TestMethod]
    [TestCategory("Diagnostics.VBRuntimeError.TypeMismatch")]
    [DataRow(42, "VBErrorValue")]
    [DataRow("ABC", "VBErrorValue")]
    [DataRow("VBErrorValue", "VBErrorValue")]
    public void EvaluateDivision_VBErrorValue_TypeMismatch(object lhs, object rhs)
    {
        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() => EvaluateDivision(CreateContext(), lhs, rhs));
    }

    private VBTypedValue EvaluateDivision(IVBExecutionContext context, object lhs, object rhs)
    {
        var lhsValue = WrapVBTypedValue(lhs, TestLocation);
        var lhsExpression = WrapLiteralExpression(lhsValue, TestLocationLHS);

        var rhsValue = WrapVBTypedValue(rhs, TestLocation);
        var rhsExpression = WrapLiteralExpression(rhsValue, TestLocationRHS);

        var expression = new VBBinaryOperatorExpression(GlobalSymbols.OperatorSymbols.Multiplication, lhsExpression, rhsExpression, TestLocation);
        return Semantics.Evaluate(context, expression, lhsValue, rhsValue)!;
    }
}
