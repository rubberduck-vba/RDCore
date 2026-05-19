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
[TestCategory("MS-VBAL 5.6.9.3.4 Binary '*' Operator")]
public class MultiplicationOperationTests : SymbolOperationTests
{
    internal override RuntimeSemantics Semantics => new BinaryMultiplicationOperatorRuntimeSemantics();
    internal override IEnumerable<VBType> EffectiveTypes => [
        VBByteType.TypeInfo,
        VBIntegerType.TypeInfo,
        VBLongType.TypeInfo,
        VBLongLongType.TypeInfo,
        VBSingleType.TypeInfo,
        VBDoubleType.TypeInfo,
        VBCurrencyType.TypeInfo,
        VBDecimalType.TypeInfo,
        VBNullType.TypeInfo,
    ];

    [TestMethod]
    [DataRow(2, 2, 4)]
    [DataRow(-2, 2, -4)]
    [DataRow(-2, -2, 4)]
    [DataRow(2, -2, -4)]
    [DataRow(0, 2, 0)]
    [DataRow(-2, 0, 0)]
    [DataRow(0, 0, 0)]
    public void EvaluateMultiplication_HappyPath_CalculatesResult(object lhs, object rhs, object expected)
    {
        var actual = EvaluateMultiplication(CreateContext(), lhs, rhs) as INumericValue;
        Assert.AreEqual(Convert.ToDouble(expected), actual?.ManagedValue);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10: Let-coercion from 'Null'")]
    [DataRow(null, 5)]   // MS-VBAL: Null * Any -> Null
    [DataRow(null, "#1-1-2026#")]   // MS-VBAL: Null * Any -> Null
    [DataRow(null, 1.23d)]   // MS-VBAL: Null * Any -> Null
    [DataRow(5, null)]   // MS-VBAL: Any * Null -> Null
    [DataRow("Empty", null)]   // MS-VBAL: Any * Null -> Null
    [DataRow("#2026-12-31#", null)]   // MS-VBAL: Any * Null -> Null
    public void EvaluateMultiplication_NullOperand_ResultIsNull(object lhs, object rhs)
    {
        // note: coercing the result to any other type would throw.
        var result = EvaluateMultiplication(CreateContext(), lhs, rhs);
        Assert.IsInstanceOfType<VBNullValue>(result);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10: Let-coercion from 'Null'")]
    public void EvaluateMultiplication_Null_LetCoercion_UDT_TypeMismatch()
    {
        var udt = new VBUserDefinedType("Test", new VBUserDefinedTypeMemberSymbol(ScopeKind.Module, new Uri("file://TestProject/TestModule/TestUDT"), "UDT", Accessibility.Public, TestLocation.Range, TestLocation.Range, new Uri("file://TestProject")));

        var lhs = VBNullValue.Null;
        var rhs = new LiteralExpression(TestLocation, new VBUserDefinedTypeValue(udt));

        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() =>
            EvaluateMultiplication(CreateContext(), lhs, rhs));
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10: Let-coercion from 'Null'")]
    public void EvaluateMultiplication_Null_LetCoercion_ResizableArray_TypeMismatch()
    {
        var lhs = VBNullValue.Null;
        var rhs = new LiteralExpression(TestLocation, VBResizableArrayValue.Empty);

        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() =>
            EvaluateMultiplication(CreateContext(), lhs, rhs));
    }

    [TestMethod]
    [DataRow(-1, "DateTime.Now")]
    [DataRow("DateTime.Now", 1)]
    [DataRow("DateTime.Now", "DateTime.Now")]
    public void EvaluateMultiplication_DateTime_ReturnsDouble(object lhs, object rhs)
    {
        var result = EvaluateMultiplication(CreateContext(), lhs, rhs);
        Assert.IsInstanceOfType<VBDoubleValue>(result);
    }

    [TestMethod]
    [DataRow("1.5", 1, 1.5d)]
    [DataRow(32767, -2.0d, -65534.0d)]
    public void EvaluateMultiplication_NumericCoercion(object lhs, object rhs, object expected)
    {
        var result = EvaluateMultiplication(CreateContext(), lhs, rhs);
        if (expected is not string)
        {
            Assert.AreEqual(Convert.ToDouble(expected), ((INumericValue)result).ManagedValue, 0.0001);
        }
    }

    [TestMethod]
    [TestCategory("Diagnostics.VBRuntimeError.Overflow")]
    [DataRow(32767, 2)]
    [DataRow(-32768, 2)]
    public void EvaluateMultiplication_Overflow(object lhs, object rhs)
    {
        Assert.Throws<VBRuntimeErrorOverflowException>(() => EvaluateMultiplication(CreateContext(), lhs, rhs));
    }

    [TestMethod]
    [TestCategory("Diagnostics.VBRuntimeError.TypeMismatch")]
    [DataRow(42, "VBErrorValue")]
    [DataRow("ABC", "VBErrorValue")]
    [DataRow("VBErrorValue", "VBErrorValue")]
    public void EvaluateMultiplication_VBErrorValue_TypeMismatch(object lhs, object rhs)
    {
        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() => EvaluateMultiplication(CreateContext(), lhs, rhs));
    }

    private VBTypedValue EvaluateMultiplication(IVBExecutionContext context, object lhs, object rhs)
    {
        var lhsValue = WrapVBTypedValue(lhs, TestLocationLHS);
        var lhsLiteral = new LiteralExpression(TestLocationLHS, lhsValue);

        var rhsValue = WrapVBTypedValue(rhs, TestLocationRHS);
        var rhsLiteral = new LiteralExpression(TestLocationRHS, rhsValue);

        var expression = new VBBinaryOperatorExpression(GlobalSymbols.Multiplication, lhsLiteral, rhsLiteral, TestLocation);
        return Semantics.Evaluate(context, expression, lhsValue, rhsValue)!;
    }
}
