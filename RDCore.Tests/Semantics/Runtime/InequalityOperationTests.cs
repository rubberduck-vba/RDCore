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
[TestCategory("MS-VBAL 5.6.9.5.2 Binary '<>' Operator")]
public class InequalityOperationTests : SymbolOperationTests
{
    internal override RuntimeSemantics Semantics => new InequalityRelationalOperatorRuntimeSemantics();
    internal override IEnumerable<VBType> EffectiveTypes => [
        VBByteType.TypeInfo,
        VBBooleanType.TypeInfo,
        VBIntegerType.TypeInfo,
        VBLongType.TypeInfo,
        VBLongLongType.TypeInfo,
        VBSingleType.TypeInfo,
        VBDoubleType.TypeInfo,
        VBCurrencyType.TypeInfo,
        VBDecimalType.TypeInfo,
        VBDateType.TypeInfo,
        VBStringType.TypeInfo,
        VBNullType.TypeInfo,
        VBErrorType.TypeInfo,
    ];

    [TestMethod]
    [DataRow(42, 42, false)]
    [DataRow(42, 43, true)]
    [DataRow(0, 0, false)]
    [DataRow(-1, 1, true)]
    public void EvaluateInequality_IntegerOperands_CalculatesResult(int lhs, int rhs, bool expected)
    {
        var actual = EvaluateInequality(CreateContext(), lhs, rhs) as VBBooleanValue;
        Assert.AreEqual(expected, actual?.Value);
    }

    [TestMethod]
    [DataRow(3.14, 3.14, false)]
    [DataRow(3.14, 2.71, true)]
    [DataRow(0.0, 0.0, false)]
    public void EvaluateInequality_DoubleOperands_CalculatesResult(double lhs, double rhs, bool expected)
    {
        var actual = EvaluateInequality(CreateContext(), lhs, rhs) as VBBooleanValue;
        Assert.AreEqual(expected, actual?.Value);
    }

    [TestMethod]
    [DataRow("hello", "hello", false)]
    [DataRow("hello", "HELLO", false)]  // Case-insensitive comparison
    [DataRow("hello", "world", true)]
    [DataRow("", "", false)]
    public void EvaluateInequality_StringOperands_CalculatesResult(string lhs, string rhs, bool expected)
    {
        var actual = EvaluateInequality(CreateContext(), lhs, rhs) as VBBooleanValue;
        Assert.AreEqual(expected, actual?.Value);
    }

    [TestMethod]
    [DataRow(true, true, false)]
    [DataRow(true, false, true)]
    [DataRow(false, false, false)]
    public void EvaluateInequality_BooleanOperands_CalculatesResult(bool lhs, bool rhs, bool expected)
    {
        var actual = EvaluateInequality(CreateContext(), lhs, rhs) as VBBooleanValue;
        Assert.AreEqual(expected, actual?.Value);
    }

    [TestMethod]
    [DataRow(1, 1, false)]
    [DataRow(1, 2, true)]
    [DataRow(0, 0, false)]
    public void EvaluateInequality_ByteOperands_CalculatesResult(object lhs, object rhs, bool expected)
    {
        var actual = EvaluateInequality(CreateContext(), Convert.ToByte(lhs), Convert.ToByte(rhs)) as VBBooleanValue;
        Assert.AreEqual(expected, actual?.Value);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10 Let-coercion from 'Null'")]
    public void EvaluateInequality_BothNullOperands_ResultIsNull()
    {
        var result = EvaluateInequality(CreateContext(), null, null);
        Assert.IsInstanceOfType<VBNullValue>(result);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10 Let-coercion from 'Null'")]
    [DataRow(null, 5)]
    [DataRow(42, null)]
    [DataRow(null, "test")]
    [DataRow("test", null)]
    public void EvaluateInequality_SingleNullOperand_ResultIsNull(object lhs, object rhs)
    {
        var result = EvaluateInequality(CreateContext(), lhs, rhs);
        Assert.IsInstanceOfType<VBNullValue>(result);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10 Let-coercion from 'Null'")]
    public void EvaluateInequality_Null_LetCoercion_UDT_TypeMismatch()
    {
        var udt = new VBUserDefinedType("Test", new VBUserDefinedTypeMemberSymbol(ScopeKind.Module, new Uri("file://TestProject/TestModule/TestUDT"), "UDT", Accessibility.Public, TestLocation.Range, TestLocation.Range, new Uri("file://TestProject")));

        var lhs = VBNullValue.Null;
        var rhs = new LiteralExpression(TestLocation, new VBUserDefinedTypeValue(udt));

        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() => EvaluateInequality(CreateContext(), lhs, rhs));
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10 Let-coercion from 'Null'")]
    public void EvaluateInequality_Null_LetCoercion_ResizableArray_TypeMismatch()
    {
        var lhs = VBNullValue.Null;
        var rhs = new LiteralExpression(TestLocation, VBResizableArrayValue.Empty);

        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() =>
            EvaluateInequality(CreateContext(), lhs, rhs));
    }

    [TestMethod]
    [DataRow("42", 42, false)]  // String "42" coerced to 42
    [DataRow("42", 43, true)]
    [DataRow(1, true, true)]  // Integer 1 compared to Boolean true (-1) -> a diagnostic is issued to LHS about the implicit conversion.
    [DataRow(0, false, false)]  // Integer 0 compared to Boolean false (0)
    public void EvaluateInequality_ImplicitCoercion(object lhs, object rhs, bool expected)
    {
        var result = EvaluateInequality(CreateContext(), lhs, rhs);
        Assert.IsInstanceOfType<VBBooleanValue>(result);
        Assert.AreEqual(expected, ((VBBooleanValue)result).Value);
    }

    [TestMethod]
    [TestCategory("Diagnostics.VBRuntimeError.TypeMismatch")]
    [DataRow(42, "VBErrorValue")]
    [DataRow("VBErrorValue", 42)]
    public void EvaluateInequality_VBErrorValue_TypeMismatch(object lhs, object rhs)
    {
        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() =>
            EvaluateInequality(CreateContext(), lhs, rhs));
    }

    private VBTypedValue EvaluateInequality(IVBExecutionContext context, object lhs, object rhs)
    {
        var lhsValue = WrapVBTypedValue(lhs, TestLocationLHS);
        var lhsExpression = WrapLiteralExpression(lhsValue, TestLocationLHS);

        var rhsValue = WrapVBTypedValue(rhs, TestLocationRHS);
        var rhsExpression = WrapLiteralExpression(rhs, TestLocationRHS);

        var expression = new VBBinaryOperatorExpression(GlobalSymbols.Inequality, lhsExpression, rhsExpression, TestLocation);
        return Semantics.Evaluate(context, expression, lhsValue, rhsValue)!;
    }
}
