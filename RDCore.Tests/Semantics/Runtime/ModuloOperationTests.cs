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
[TestCategory("MS-VBAL 5.6.9.3.6 Binary 'Mod' Operator")]
public class ModuloOperationTests : BinaryOperatorOperationTests
{
    protected override BinaryOperatorSymbol Symbol => GlobalSymbols.OperatorSymbols.ModuloOp;
    internal override IRuntimeSemantics Semantics => new BinaryModuloOperatorRuntimeSemantics();
    internal override IEnumerable<VBType> EffectiveTypes => [
        VBByteType.TypeInfo,
        VBIntegerType.TypeInfo,
        VBLongType.TypeInfo,
        VBLongLongType.TypeInfo,
        VBNullType.TypeInfo,
    ];

    [TestMethod]
    [DataRow(24, 12, 0)]
    [DataRow(15, 2, 1)]
    [DataRow(24, 5, 4)]
    public void Operator_EvaluatesOp(object lhs, object rhs, object expected)
    {
        var actual = EvaluateBinaryOp(CreateContext(), lhs, rhs) as INumericValue;
        Assert.AreEqual(Convert.ToDouble(expected), actual?.ManagedValue);
    }

    [TestMethod]
    [TestCategory("Diagnostics.VBRuntimeError.DivisionByZero")]
    [DataRow(1, 0)]
    [DataRow(-1, 0)]
    [DataRow(2.5, 0.5)]
    public void EvaluateModulo_DivisionByZero(object lhs, object rhs)
    {
        Assert.Throws<VBRuntimeErrorDivisionByZeroException>(() => EvaluateBinaryOp(CreateContext(), lhs, rhs));
    }

    [TestMethod]
    [DataRow("32768", "DateTime.Now")]
    [DataRow("DateTime.Now", 1)]
    [DataRow("DateTime.Now", "DateTime.Now")]
    public void EvaluateModulo_GivenDateTimeLHS_ReturnsLong(object lhs, object rhs)
    {
        var result = EvaluateBinaryOp(CreateContext(), lhs, rhs);
        Assert.IsInstanceOfType<VBLongValue>(result);
    }

    [TestMethod]
    [DataRow(42, "DateTime.Now")]
    public void EvaluateModulo_GivenDateTimeRHS_ReturnsInteger(object lhs, object rhs)
    {
        var result = EvaluateBinaryOp(CreateContext(), lhs, rhs);
        Assert.IsInstanceOfType<VBIntegerValue>(result);
    }

    [TestMethod]
    [DataRow(10, "DateTime.Now")]
    public void EvaluateModulo_DateTimeRHS_ReturnsInteger(object lhs, object rhs)
    {
        var result = EvaluateBinaryOp(CreateContext(), lhs, rhs);
        Assert.IsInstanceOfType<VBIntegerValue>(result);
    }

    [TestMethod]
    [DataRow("Empty", (byte)10, Tokens.Integer)]
    [DataRow(32767, "DateTime.Now", Tokens.Integer)]
    [DataRow(true, 2, Tokens.Integer)]
    [DataRow(32767, 2.5d, Tokens.Integer)]
    [DataRow(123.456d, 2.5d, Tokens.Long)]
    [DataRow("123.456", 2, Tokens.Long)]
    [DataRow("DateTime.Now", 2, Tokens.Long)]
    [DataRow((long)123456, 42, Tokens.LongLong)]
    [DataRow((long)123456, "42", Tokens.LongLong)]
    [DataRow((long)123456, "DateTime.Now", Tokens.LongLong)]
    public void EvaluateModulo_DataType(object lhs, object rhs, string type)
    {
        var result = EvaluateBinaryOp(CreateContext(), lhs, rhs);
        Assert.AreEqual(type, result.TypeInfo.Name);
    }
}
