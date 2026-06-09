using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Semantics.Runtime.Abstract;
using RDCore.SDK.Semantics.Runtime.Operators;

namespace RDCore.Tests.Semantics.Runtime;

[TestClass]
[TestCategory("MS-VBAL 5.6.9.8.1 Unary 'Not' Operator")]
public class UnaryNotOperationTests : UnaryOperatorOperationTests
{
    protected override UnaryOperatorSymbol Symbol => GlobalSymbols.OperatorSymbols.BitwiseNotOp;
    internal override IRuntimeSemantics Semantics => new UnaryNotOperatorRuntimeSemantics();
    internal override IEnumerable<VBType> EffectiveTypes => [
        VBBooleanType.TypeInfo,
        VBByteType.TypeInfo,
        VBIntegerType.TypeInfo,
        VBLongType.TypeInfo,
        VBLongLongType.TypeInfo,
        VBNullType.TypeInfo,
    ];

    [TestMethod]
    [DataRow(0, -1)]
    [DataRow(-1, 0)]
    [DataRow(5, -6)]
    [DataRow(-5, 4)]
    public void Operator_IntegerOperand_CalculatesResult(object operand, int expected)
    {
        var actual = EvaluateUnaryOp(CreateContext(), operand) as VBIntegerValue;
        Assert.AreEqual(expected, actual?.ManagedValue);
    }

    [TestMethod]
    [DataRow(true, 0)]
    [DataRow(false, -1)]
    public void Operator_BooleanOperand_CalculatesResult(object operand, object expected)
    {
        var actual = EvaluateUnaryOp(CreateContext(), operand) as VBIntegerValue;
        Assert.AreEqual(Convert.ToInt16(expected), actual?.Value);
    }
}
