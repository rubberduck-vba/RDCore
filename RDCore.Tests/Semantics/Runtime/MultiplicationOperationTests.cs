using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;
using RDCore.SDK.Semantics.Runtime.Abstract;
using RDCore.SDK.Semantics.Runtime.Operators;

namespace RDCore.Tests.Semantics.Runtime;


[TestClass]
[TestCategory("MS-VBAL 5.6.9.3.4 Binary '*' Operator")]
public class MultiplicationOperationTests : BinaryImpOperationTests
{
    protected override BinaryOperatorSymbol Symbol => GlobalSymbols.OperatorSymbols.MultiplicationOp;
    internal override IRuntimeSemantics Semantics => new BinaryMultiplicationOperatorRuntimeSemantics();
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
    public void Operator_EvaluatesOp(object lhs, object rhs, object expected)
    {
        var actual = EvaluateMultiplication(CreateContext(), lhs, rhs) as INumericValue;
        Assert.AreEqual(Convert.ToDouble(expected), actual?.ManagedValue);
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
        var lhsLiteral = new VBLiteralExpression(TestLocationLHS, lhsValue);

        var rhsValue = WrapVBTypedValue(rhs, TestLocationRHS);
        var rhsLiteral = new VBLiteralExpression(TestLocationRHS, rhsValue);

        var expression = new VBBinaryOperatorExpression(GlobalSymbols.OperatorSymbols.MultiplicationOp, TestLocation, lhsLiteral, rhsLiteral);
        return Semantics.Evaluate(context, expression, lhsValue, rhsValue)!;
    }
}
