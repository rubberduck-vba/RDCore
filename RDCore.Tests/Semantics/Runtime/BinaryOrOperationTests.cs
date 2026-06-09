using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Semantics.Runtime.Abstract;
using RDCore.SDK.Semantics.Runtime.Operators;

namespace RDCore.Tests.Semantics.Runtime;

[TestClass]
[TestCategory("MS-VBAL 5.6.9.8.3 Binary 'Or' Operator")]
public class BinaryOrOperationTests : BinaryOperatorOperationTests
{
    protected override BinaryOperatorSymbol Symbol => GlobalSymbols.OperatorSymbols.BitwiseOrOp;
    internal override IRuntimeSemantics Semantics => new BinaryOrBitwiseOperatorRuntimeSemantics();
    internal override IEnumerable<VBType> EffectiveTypes => [
        VBBooleanType.TypeInfo,
        VBByteType.TypeInfo,
        VBIntegerType.TypeInfo,
        VBLongType.TypeInfo,
        VBLongLongType.TypeInfo,
        VBNullType.TypeInfo,
    ];

    [TestMethod]
    [DataRow(false, false, false)]
    [DataRow(false, true, true)]
    [DataRow(true, false, true)]
    [DataRow(true, true, true)]
    public void Operator_BooleanContext_EvaluatesOp(object lhs, object rhs, bool expected)
    {
        var actual = EvaluateBinaryOp(CreateContext(), lhs, rhs) as VBBooleanValue;
        Assert.AreEqual(expected, actual?.Value);
    }

    [TestMethod]
    [DataRow(0, 0, 0)]
    [DataRow(0, -1, -1)]
    [DataRow(-1, 0, -1)]
    [DataRow(-1, -1, -1)]
    public void Operator_BitwiseContext_EvaluatesOp(object lhs, object rhs, int expected)
    {
        var actual = EvaluateBinaryOp(CreateContext(), lhs, rhs) as VBIntegerValue;
        Assert.AreEqual(expected, actual?.ManagedValue);
    }

    [TestMethod]
    [DataRow(5, 3, 7)]
    [DataRow(12, 10, 14)]
    [DataRow(15, 0, 15)] 
    public void Operator_IntegerBitwise_EvaluatesOp(object lhs, object rhs, int expected)
    {
        var actual = EvaluateBinaryOp(CreateContext(), lhs, rhs) as VBIntegerValue;
        Assert.AreEqual(expected, actual?.ManagedValue);
    }
}
