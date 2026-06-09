using RDCore.SDK.Model;
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
[TestCategory("MS-VBAL 5.6.9.3.6 Binary '\\' Operator")]
public class IntegerDivisionOperationTests : BinaryOperatorOperationTests
{
    protected override BinaryOperatorSymbol Symbol => GlobalSymbols.OperatorSymbols.IntegerDivisionOp;

    internal override IRuntimeSemantics Semantics => new BinaryIntegerDivisionOperatorRuntimeSemantics();
    internal override IEnumerable<VBType> EffectiveTypes => [
        VBByteType.TypeInfo,
        VBIntegerType.TypeInfo,
        VBLongType.TypeInfo,
        VBLongLongType.TypeInfo,
        VBNullType.TypeInfo,
    ];

    [TestMethod]
    [DataRow(2, 2, 1)]
    [DataRow(-2, 2, -1)]
    [DataRow(-2, -2, 1)]
    [DataRow(2, -2, -1)]
    [DataRow(12, 2, 6)]
    [DataRow(5, 2, 2)]
    public void Operator_EvaluatesOp(object lhs, object rhs, object expected)
    {
        var actual = EvaluateBinaryOp(CreateContext(), lhs, rhs) as INumericValue;
        Assert.AreEqual(Convert.ToDouble(expected), actual?.ManagedValue);
    }

    [TestMethod]
    [TestCategory("Diagnostics.VBRuntimeError.DivisionByZero")]
    [DataRow(1, 0)]
    [DataRow(-1, 0)]
    public void EvaluateOp_IntegerDivisionByZero(object lhs, object rhs)
    {
        Assert.Throws<VBRuntimeErrorDivisionByZeroException>(() => EvaluateBinaryOp(CreateContext(), lhs, rhs));
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10: Let-coercion from 'Null'")]
    [DataRow(null, 5)]
    [DataRow(null, "#1-1-2026#")]
    [DataRow(null, 1.23d)]   
    [DataRow(5, null)]   
    [DataRow("Empty", null)] 
    [DataRow("#2026-12-31#", null)]   
    public void EvaluateOp_NullOperand_ResultIsNull(object lhs, object rhs)
    {
        // note: coercing the result to any other type would throw.
        var result = EvaluateBinaryOp(CreateContext(), lhs, rhs);
        Assert.IsInstanceOfType<VBNullValue>(result);
    }

    [TestMethod]
    [DataRow("DateTime.Now", 1)]
    [DataRow("DateTime.Now", "DateTime.Now")]
    public void EvaluateIntegerDivision_DateTimeLHS_ReturnsLong(object lhs, object rhs)
    {
        var result = EvaluateBinaryOp(CreateContext(), lhs, rhs);
        Assert.IsInstanceOfType<VBLongValue>(result);
    }

    [TestMethod]
    [DataRow(42, "DateTime.Now")]
    public void EvaluateIntegerDivision_DateTimeRHS_ReturnsInteger(object lhs, object rhs)
    {
        var result = EvaluateBinaryOp(CreateContext(), lhs, rhs);
        Assert.IsInstanceOfType<VBIntegerValue>(result);
    }

    [TestMethod]
    [DataRow(32767, "DateTime.Now", Tokens.Integer)]
    [DataRow("Empty", (byte)10, Tokens.Integer)]
    [DataRow(true, 2, Tokens.Integer)]
    [DataRow(32767, 2.5d, Tokens.Integer)]
    [DataRow(123.456d, 2.5d, Tokens.Long)]
    [DataRow("123.456", 2, Tokens.Long)]
    [DataRow("DateTime.Now", 2, Tokens.Long)]
    [DataRow((long)123456, 42, Tokens.LongLong)]
    [DataRow((long)123456, "42", Tokens.LongLong)]
    [DataRow((long)123456, "DateTime.Now", Tokens.LongLong)]
    public void EvaluateIntegerDivision_DataType(object lhs, object rhs, string type)
    {
        var result = EvaluateBinaryOp(CreateContext(), lhs, rhs);
        Assert.AreEqual(type, result.TypeInfo.Name);
    }

    [TestMethod]
    [TestCategory("Diagnostics.VBRuntimeError.Overflow")]
    [DataRow(32767, 2)]
    [DataRow(-32768, 2)]
    public void EvaluateIntegerDivision_Overflow(object lhs, object rhs)
    {
        Assert.Throws<VBRuntimeErrorOverflowException>(() => EvaluateBinaryOp(CreateContext(), lhs, rhs));
    }
}
