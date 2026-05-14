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
[TestCategory("MS-VBAL 5.6.9.5.1 Binary '=' Operator")]
public class EqualityOperationTests : SymbolOperationTests
{
    internal override RuntimeSemantics Semantics => new EqualityRelationalOperatorRuntimeSemantics();
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
    [DataRow(42, 42, true)]
    [DataRow(42, 43, false)]
    [DataRow(0, 0, true)]
    [DataRow(-1, -1, true)]
    public void EvaluateEquality_IntegerOperands_CalculatesResult(int lhs, int rhs, bool expected)
    {
        var actual = EvaluateEquality(CreateContext(), lhs, rhs) as VBBooleanValue;
        Assert.AreEqual(expected, actual?.Value);
    }

    [TestMethod]
    [DataRow(3.14, 3.14, true)]
    [DataRow(3.14, 2.71, false)]
    [DataRow(0.0, 0.0, true)]
    public void EvaluateEquality_DoubleOperands_CalculatesResult(double lhs, double rhs, bool expected)
    {
        var actual = EvaluateEquality(CreateContext(), lhs, rhs) as VBBooleanValue;
        Assert.AreEqual(expected, actual?.Value);
    }

    [TestMethod]
    [DataRow("hello", "hello", true)]
    [DataRow("hello", "HELLO", true)]  // Case-insensitive comparison
    [DataRow("hello", "world", false)]
    [DataRow("", "", true)]
    public void EvaluateEquality_StringOperands_CalculatesResult(string lhs, string rhs, bool expected)
    {
        var actual = EvaluateEquality(CreateContext(), lhs, rhs) as VBBooleanValue;
        Assert.AreEqual(expected, actual?.Value);
    }

    [TestMethod]
    [DataRow(true, true, true)]
    [DataRow(true, false, false)]
    [DataRow(false, false, true)]
    public void EvaluateEquality_BooleanOperands_CalculatesResult(bool lhs, bool rhs, bool expected)
    {
        var actual = EvaluateEquality(CreateContext(), lhs, rhs) as VBBooleanValue;
        Assert.AreEqual(expected, actual?.Value);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10 Let-coercion from 'Null'")]
    public void EvaluateEquality_BothNullOperands_ResultIsNull()
    {
        var result = EvaluateEquality(CreateContext(), null, null);
        Assert.IsInstanceOfType<VBNullValue>(result);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10 Let-coercion from 'Null'")]
    [DataRow(null, 5)]
    [DataRow(42, null)]
    [DataRow(null, "test")]
    [DataRow("test", null)]
    public void EvaluateEquality_SingleNullOperand_ResultIsNull(object lhs, object rhs)
    {
        var result = EvaluateEquality(CreateContext(), lhs, rhs);
        Assert.IsInstanceOfType<VBNullValue>(result);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10 Let-coercion from 'Null'")]
    public void EvaluateEquality_Null_LetCoercion_UDT_TypeMismatch()
    {
        var udt = new VBUserDefinedType("Test", new VBUserDefinedTypeMember(new Uri("file://TestProject/TestModule/TestUDT"), "TestUDT", TestLocation.Range, TestLocation.Range, new Uri("file://TestProject")));

        var lhs = VBNullValue.Null;
        var rhs = new LiteralExpression<VBUserDefinedTypeValue>(TestLocation)
            .WithResultValue(new VBUserDefinedTypeValue(udt));

        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() =>
            EvaluateEquality(CreateContext(), lhs, rhs));
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10 Let-coercion from 'Null'")]
    public void EvaluateEquality_Null_LetCoercion_ResizableArray_TypeMismatch()
    {
        var lhs = VBNullValue.Null;
        var rhs = new LiteralExpression<VBResizableArrayValue>(TestLocation)
            .WithResultValue(new VBResizableArrayValue(0, 0, VBIntegerType.TypeInfo));

        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() =>
            EvaluateEquality(CreateContext(), lhs, rhs));
    }

    [TestMethod]
    [DataRow(1, true, false)]  // Integer 1 compared to Boolean true (coerced to -1) -> false because runtime semantics here require value equality. a diagnostic is issued here about the implicit conversion, this behavior is observed in MS-VBA.
    [DataRow("42", 42, true)]  // String "42" coerced to 42
    [DataRow("42", 43, false)]
    [DataRow(0, false, true)]  // Integer 0 compared to Boolean false (0)
    public void EvaluateEquality_ImplicitCoercion(object lhs, object rhs, bool expected)
    {
        var result = EvaluateEquality(CreateContext(), lhs, rhs);
        Assert.IsInstanceOfType<VBBooleanValue>(result);
        Assert.AreEqual(expected, ((VBBooleanValue)result).Value);
    }

    [TestMethod]
    [TestCategory("Diagnostics.VBRuntimeError.TypeMismatch")]
    [DataRow(42, "VBErrorValue")]
    [DataRow("VBErrorValue", 42)]
    public void EvaluateEquality_VBErrorValue_TypeMismatch(object lhs, object rhs)
    {
        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() =>
            EvaluateEquality(CreateContext(), lhs, rhs));
    }

    private VBTypedValue EvaluateEquality(VBExecutionContext context, object lhs, object rhs)
    {
        var lhsValue = WrapLiteralExpression(lhs, TestLocationLHS);
        var rhsValue = WrapLiteralExpression(rhs, TestLocationRHS);
        var expression = new VBBinaryOperatorExpression(GlobalSymbols.Equality, lhsValue, rhsValue, TestLocation);

        return Semantics.Evaluate(context, expression, lhsValue.ResultValue, rhsValue.ResultValue)!;
    }
}
