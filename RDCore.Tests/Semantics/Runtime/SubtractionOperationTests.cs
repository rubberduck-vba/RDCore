using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Semantics.Runtime.Abstract;
using RDCore.SDK.Semantics.Runtime.Operators;

namespace RDCore.Tests.Semantics.Runtime;

[TestClass]
[TestCategory("MS-VBAL 5.6.9.3.3 Binary '-' Operator")]
public class SubtractionOperationTests : BinaryOperatorOperationTests
{
    protected override BinaryOperatorSymbol Symbol => GlobalSymbols.OperatorSymbols.SubtractionOp;
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
    public void Operator_EvaluatesOp(object lhs, object rhs, object expected)
    {
        var actual = EvaluateBinaryOp(CreateContext(), lhs, rhs) as INumericValue;
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
    public void Operator_NullOperand_ResultIsNull(object lhs, object rhs)
    {
        // note: coercing the result to any other type would throw.
        var result = EvaluateBinaryOp(CreateContext(), lhs, rhs);
        Assert.IsInstanceOfType<VBNullValue>(result);
    }

    [TestMethod]
    [TestCategory("Diagnostics.VBRuntimeError.Overflow")]
    [DataRow(32767, -1)]
    [DataRow(-32768, 1)]
    public void Operator_EvaluatesOp_Overflow(object lhs, object rhs)
    {
        Assert.Throws<VBRuntimeErrorOverflowException>(() => EvaluateBinaryOp(CreateContext(), lhs, rhs));
    }

    [TestMethod]
    [TestCategory("Diagnostics.VBRuntimeError.TypeMismatch")]
    [DataRow("1.5", "1")]
    [DataRow(42, "VBErrorValue")]
    [DataRow("ABC", "VBErrorValue")]
    [DataRow("VBErrorValue", "VBErrorValue")]
    public void Operator_EvaluatesOp_TypeMismatch(object lhs, object rhs)
    {
        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() => EvaluateBinaryOp(CreateContext(), lhs, rhs));
    }

    [TestMethod]
    [DataRow(-1, "DateTime.Now")]
    [DataRow("DateTime.Now", 1)]
    [DataRow("DateTime.Now", "DateTime.Now")]
    public void Operator_EvaluatesOp_DateTime_ReturnsDateTime(object lhs, object rhs)
    {
        var result = EvaluateBinaryOp(CreateContext(), lhs, rhs);
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
    public void Operator_EvaluatesOp_OperandsEmpty_ResultsIsIntegerZero()
    {
        var result = EvaluateBinaryOp(CreateContext(), "Empty", "Empty");

        Assert.IsInstanceOfType<VBIntegerValue>(result);
        Assert.AreEqual(0, ((VBIntegerValue)result).Value);
    }
}
