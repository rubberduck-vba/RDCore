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
[TestCategory("MS-VBAL 5.6.9.3.7 Binary '^' Operator")]
public class ExponentOperationTests : BinaryOperatorOperationTests
{
    protected override BinaryOperatorSymbol Symbol => GlobalSymbols.OperatorSymbols.ExponentiationOp;
    internal override IRuntimeSemantics Semantics => new BinaryExponentOperatorRuntimeSemantics();
    internal override IEnumerable<VBType> EffectiveTypes => [
        VBDoubleType.TypeInfo,
        VBNullType.TypeInfo,
    ];

    [TestMethod]
    [DataRow(2, 2, 4)]
    [DataRow(2, 3, 8)]
    [DataRow(10, 2, 100)]
    public void EvaluateExponentiation_HappyPath_CalculatesResult(object lhs, object rhs, object expected)
    {
        var actual = EvaluateBinaryOp(CreateContext(), lhs, rhs) as INumericValue;
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
    public void EvaluateExponentiation_NullOperand_ResultIsNull(object lhs, object rhs)
    {
        // note: coercing the result to any other type would throw.
        var result = EvaluateBinaryOp(CreateContext(), lhs, rhs);
        Assert.IsInstanceOfType<VBNullValue>(result);
    }

    [TestMethod]
    [DataRow(-1, "DateTime.Now")]
    [DataRow("DateTime.Now", 1)]
    [DataRow("DateTime.Now", "DateTime.Now")]
    public void EvaluateExponentiation_DateTime_ReturnsDouble(object lhs, object rhs)
    {
        var result = EvaluateBinaryOp(CreateContext(), lhs, rhs);
        Assert.IsInstanceOfType<VBDoubleValue>(result);
    }
}
