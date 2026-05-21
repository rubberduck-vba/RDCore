using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Expressions.Operators;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;
using RDCore.SDK.Semantics.Runtime.Abstract;
using RDCore.SDK.Semantics.Runtime.Operators;

namespace RDCore.Tests.Semantics.Runtime;

[TestClass]
[TestCategory("MS-VBAL 5.6.9.3.3 Binary '-' Operator")]
public class SubtractionOperationTests : SymbolOperationTests
{
    internal override IRuntimeSemantics Semantics => new BinarySubtractionOperatorRuntimeSematics();
    internal override IEnumerable<VBType> EffectiveTypes => [
        VBByteType.TypeInfo,
        VBIntegerType.TypeInfo,
        VBLongType.TypeInfo,
        VBLongLongType.TypeInfo,
        VBSingleType.TypeInfo,
        VBDoubleType.TypeInfo,
        VBCurrencyType.TypeInfo,
        VBDecimalType.TypeInfo,
        VBDateType.TypeInfo,
        VBNullType.TypeInfo,
    ];

    [TestMethod]
    [DataRow(2, 2, 0)]
    [DataRow(-2, 2, -4)]
    [DataRow(-2, -2, 0)]
    [DataRow(2, -2, 4)]
    [DataRow(0, -2, 2)]
    [DataRow(-2, 0, -2)]
    [DataRow(0, 0, 0)]
    public void EvaluateSubtraction_HappyPath_CalculatesResult(object lhs, object rhs, object expected)
    {
        var actual = EvaluateSubtraction(CreateContext(), lhs, rhs) as INumericValue;
        Assert.AreEqual(Convert.ToDouble(expected), actual?.ManagedValue);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10: Let-coercion from 'Null'")]
    [DataRow(null, 5)]   // MS-VBAL: Null - Any -> Null
    [DataRow(null, "#1-1-2026#")]   // MS-VBAL: Null - Any -> Null
    [DataRow(null, 1.23d)]   // MS-VBAL: Null - Any -> Null
    [DataRow(5, null)]   // MS-VBAL: Any - Null -> Null
    [DataRow("Empty", null)]   // MS-VBAL: Any - Null -> Null
    [DataRow("#2026-12-31#", null)]   // MS-VBAL: Any - Null -> Null
    public void EvaluateSubtraction_NullOperand_ResultIsNull(object lhs, object rhs)
    {
        // note: coercing the result to any other type would throw.
        var result = EvaluateSubtraction(CreateContext(), lhs, rhs);
        Assert.IsInstanceOfType<VBNullValue>(result);
    }

    [TestMethod]
    [TestCategory("Diagnostics.VBRuntimeError.Overflow")]
    [DataRow(32767, -1)]
    [DataRow(-32768, 1)]
    public void EvaluateSubtraction_Overflow(object lhs, object rhs)
    {
        Assert.Throws<VBRuntimeErrorOverflowException>(() => EvaluateSubtraction(CreateContext(), lhs, rhs));
    }

    [TestMethod]
    [TestCategory("Diagnostics.VBRuntimeError.TypeMismatch")]
    [DataRow("1.5", "1")]
    [DataRow(42, "VBErrorValue")]
    [DataRow("ABC", "VBErrorValue")]
    [DataRow("VBErrorValue", "VBErrorValue")]
    public void EvaluateSubtraction_TypeMismatch(object lhs, object rhs)
    {
        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() => EvaluateSubtraction(CreateContext(), lhs, rhs));
    }

    [TestMethod]
    [DataRow(-1, "DateTime.Now")]
    [DataRow("DateTime.Now", 1)]
    [DataRow("DateTime.Now", "DateTime.Now")]
    public void EvaluateSubtraction_DateTime_ReturnsDateTime(object lhs, object rhs)
    {
        var result = EvaluateSubtraction(CreateContext(), lhs, rhs);
        if (string.Equals(lhs, "DateTime.Now") && string.Equals(rhs, "DateTime.Now"))
        {
            Assert.IsInstanceOfType<VBDoubleValue>(result);
        }
        else
        {
            Assert.IsInstanceOfType<VBDateValue>(result);
        }
    }

    [TestMethod]
    public void EvaluateSubtraction_BothEmpty_ResultsIsIntegerZero()
    {
        var result = EvaluateSubtraction(CreateContext(), "Empty", "Empty");

        Assert.IsInstanceOfType<VBIntegerValue>(result);
        Assert.AreEqual(0, ((VBIntegerValue)result).Value);
    }

    private VBTypedValue EvaluateSubtraction(IVBExecutionContext context, object lhs, object rhs)
    {
        var lhsValue = WrapVBTypedValue(lhs, TestLocationLHS);
        var lhsExpression = WrapLiteralExpression(lhsValue, TestLocationLHS);

        var rhsValue = WrapVBTypedValue(rhs, TestLocationRHS);
        var rhsExpression = WrapLiteralExpression(rhsValue, TestLocationRHS);

        var expression = new VBBinaryOperatorExpression(GlobalSymbols.OperatorSymbols.Subtraction, lhsExpression, rhsExpression, TestLocation);
        return Semantics.Evaluate(context, expression, lhsValue, rhsValue)!;
    }
}
