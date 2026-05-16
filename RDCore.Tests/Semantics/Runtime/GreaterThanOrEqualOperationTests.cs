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
[TestCategory("MS-VBAL 5.6.9.5.6 Binary '>=' Operator")]
public class GreaterThanOrEqualOperationTests : SymbolOperationTests
{
    internal override RuntimeSemantics Semantics => new GreaterThanEqRelationalOperatorRuntimeSemantics();
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
    [DataRow(20, 10, true)]
    [DataRow(10, 20, false)]
    [DataRow(10, 10, true)]
    [DataRow(0, -5, true)]
    public void EvaluateGreaterThanOrEqual_IntegerOperands_CalculatesResult(int lhs, int rhs, bool expected)
    {
        var actual = EvaluateGreaterThanOrEqual(CreateContext(), lhs, rhs) as VBBooleanValue;
        Assert.AreEqual(expected, actual?.Value);
    }

    [TestMethod]
    [DataRow(2.5, 1.5, true)]
    [DataRow(1.5, 2.5, false)]
    [DataRow(1.5, 1.5, true)]
    [DataRow(-2.5, -3.5, true)]
    public void EvaluateGreaterThanOrEqual_DoubleOperands_CalculatesResult(double lhs, double rhs, bool expected)
    {
        var actual = EvaluateGreaterThanOrEqual(CreateContext(), lhs, rhs) as VBBooleanValue;
        Assert.AreEqual(expected, actual?.Value);
    }

    [TestMethod]
    [DataRow("banana", "apple", true)]
    [DataRow("apple", "banana", false)]
    [DataRow("apple", "apple", true)]
    [DataRow("aab", "aaa", true)]
    public void EvaluateGreaterThanOrEqual_StringOperands_CalculatesResult(string lhs, string rhs, bool expected)
    {
        var actual = EvaluateGreaterThanOrEqual(CreateContext(), lhs, rhs) as VBBooleanValue;
        Assert.AreEqual(expected, actual?.Value);
    }

    [TestMethod]
    [DataRow(false, true, true)]
    [DataRow(true, false, false)]
    [DataRow(false, false, true)]
    public void EvaluateGreaterThanOrEqual_BooleanOperands_CalculatesResult(bool lhs, bool rhs, bool expected)
    {
        var actual = EvaluateGreaterThanOrEqual(CreateContext(), lhs, rhs) as VBBooleanValue;
        Assert.AreEqual(expected, actual?.Value);
    }

    [TestMethod]
    [DataRow(2, 1, true)]
    [DataRow(1, 2, false)]
    [DataRow(5, 5, true)]
    public void EvaluateGreaterThanOrEqual_ByteOperands_CalculatesResult(object lhs, object rhs, bool expected)
    {
        var actual = EvaluateGreaterThanOrEqual(CreateContext(), Convert.ToByte(lhs), Convert.ToByte(rhs)) as VBBooleanValue;
        Assert.AreEqual(expected, actual?.Value);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10 Let-coercion from 'Null'")]
    public void EvaluateGreaterThanOrEqual_BothNullOperands_ResultIsNull()
    {
        var result = EvaluateGreaterThanOrEqual(CreateContext(), null, null);
        Assert.IsInstanceOfType<VBNullValue>(result);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10 Let-coercion from 'Null'")]
    [DataRow(null, 5)]
    [DataRow(42, null)]
    [DataRow(null, "test")]
    [DataRow("test", null)]
    public void EvaluateGreaterThanOrEqual_SingleNullOperand_ResultIsNull(object lhs, object rhs)
    {
        var result = EvaluateGreaterThanOrEqual(CreateContext(), lhs, rhs);
        Assert.IsInstanceOfType<VBNullValue>(result);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10 Let-coercion from 'Null'")]
    public void EvaluateGreaterThanOrEqual_Null_LetCoercion_UDT_TypeMismatch()
    {
        var udt = new VBUserDefinedType("Test", new VBUserDefinedTypeMember(new Uri("file://TestProject/TestModule/TestUDT"), "TestUDT", TestLocation.Range, TestLocation.Range, new Uri("file://TestProject")));

        var lhs = VBNullValue.Null;
        var rhs = new LiteralExpression(TestLocation, new VBUserDefinedTypeValue(udt));

        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() =>
            EvaluateGreaterThanOrEqual(CreateContext(), lhs, rhs));
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10 Let-coercion from 'Null'")]
    public void EvaluateGreaterThanOrEqual_Null_LetCoercion_ResizableArray_TypeMismatch()
    {
        var lhs = VBNullValue.Null;
        var rhs = new LiteralExpression(TestLocation, new VBResizableArrayValue(0, 0, VBIntegerType.TypeInfo));

        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() =>
            EvaluateGreaterThanOrEqual(CreateContext(), lhs, rhs));
    }

    [TestMethod]
    [DataRow("20", 10, true)]
    [DataRow("10", 20, false)]
    [DataRow("10", 10, true)]
    public void EvaluateGreaterThanOrEqual_ImplicitCoercion(object lhs, object rhs, bool expected)
    {
        var result = EvaluateGreaterThanOrEqual(CreateContext(), lhs, rhs);
        Assert.IsInstanceOfType<VBBooleanValue>(result);
        Assert.AreEqual(expected, ((VBBooleanValue)result).Value);
    }

    [TestMethod]
    [TestCategory("Diagnostics.VBRuntimeError.TypeMismatch")]
    [DataRow(42, "VBErrorValue")]
    [DataRow("VBErrorValue", 42)]
    public void EvaluateGreaterThanOrEqual_VBErrorValue_TypeMismatch(object lhs, object rhs)
    {
        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() =>
            EvaluateGreaterThanOrEqual(CreateContext(), lhs, rhs));
    }

    private VBTypedValue EvaluateGreaterThanOrEqual(VBExecutionContext context, object lhs, object rhs)
    {
        var lhsValue = WrapLiteralExpression(lhs, TestLocationLHS);
        var rhsValue = WrapLiteralExpression(rhs, TestLocationRHS);
        var expression = new VBBinaryOperatorExpression(GlobalSymbols.GreaterThanOrEqual, lhsValue, rhsValue, TestLocation);

        return Semantics.Evaluate(context, expression, lhsValue.RuntimeValue, rhsValue.RuntimeValue)!;
    }
}
