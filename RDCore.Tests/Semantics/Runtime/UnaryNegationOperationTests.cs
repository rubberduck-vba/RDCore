using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Semantics.Runtime.Abstract;
using RDCore.SDK.Semantics.Runtime.Operators;

namespace RDCore.Tests.Semantics.Runtime;

[TestClass]
[TestCategory("MS-VBAL 5.6.9.3.1 Unary '-' Operator")]
public class UnaryNegationOperationTests : UnaryOperatorOperationTests
{
    protected override UnaryOperatorSymbol Symbol =>  GlobalSymbols.OperatorSymbols.UnaryNegationOp;
    internal override IRuntimeSemantics Semantics => new UnaryNegationOperatorRuntimeSemantics();
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
    [DataRow(5, -5)]
    [DataRow(-5, 5)]
    [DataRow(0, 0)]
    [DataRow(32767, -32767)]
    public void Operator_IntegerContext_EvaluatesOp(object operand, int expected)
    {
        var actual = EvaluateUnaryOp(CreateContext(), operand) as VBIntegerValue;
        Assert.AreEqual(expected, actual?.ManagedValue);
    }

    [TestMethod]
    [DataRow(3.5, -3.5)]
    [DataRow(-2.5, 2.5)]
    [DataRow(0.0, -0.0)]
    public void Operator_DoubleContext_EvaluatesOp(object operand, double expected)
    {
        var actual = EvaluateUnaryOp(CreateContext(), operand) as VBDoubleValue;
        Assert.AreEqual(expected, actual?.ManagedValue);
    }
}
