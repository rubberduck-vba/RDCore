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
[TestCategory("MS-VBAL 5.6.9.3.5 Binary '/' Operator")]
public class DivisionOperationTests : BinaryOperatorOperationTests
{
    protected override BinaryOperatorSymbol Symbol => GlobalSymbols.OperatorSymbols.DivisionOp;
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
    public void Operator_CalculatesResult(object lhs, object rhs, object expected)
    {
        var actual = EvaluateBinaryOp(CreateContext(), lhs, rhs) as INumericValue;
        Assert.AreEqual(Convert.ToDouble(expected), actual?.ManagedValue);
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
    public void Operator_NullContext_EvaluatesOp_NullResult(object lhs, object rhs)
    {
        // note: coercing the result to any other type would throw.
        var result = EvaluateBinaryOp(CreateContext(), lhs, rhs);
        Assert.IsInstanceOfType<VBNullValue>(result);
    }

    [TestMethod]
    [DataRow(-1, "DateTime.Now")]
    [DataRow("DateTime.Now", 1)]
    [DataRow("DateTime.Now", "DateTime.Now")]
    public void Operator_DateContext_EvaluatesOp(object lhs, object rhs)
    {
        var result = EvaluateBinaryOp(CreateContext(), lhs, rhs);
        Assert.IsInstanceOfType<VBDoubleValue>(result);
    }

    [TestMethod]
    [TestCategory("Diagnostics.VBRuntimeError.Overflow")]
    [DataRow(32767, 2)]
    [DataRow(-32768, 2)]
    [DataRow(0, 0)]
    public void Operator_Overflow(object lhs, object rhs)
    {
        Assert.Throws<VBRuntimeErrorOverflowException>(() => EvaluateBinaryOp(CreateContext(), lhs, rhs));
    }

    [TestMethod]
    [TestCategory("Diagnostics.VBRuntimeError.DivisionByZero")]
    [DataRow(1, 0)]
    [DataRow(-1, 0)]
    public void Operator_DivisionByZero(object lhs, object rhs)
    {
        Assert.Throws<VBRuntimeErrorDivisionByZeroException>(() => EvaluateBinaryOp(CreateContext(), lhs, rhs));
    }
}
