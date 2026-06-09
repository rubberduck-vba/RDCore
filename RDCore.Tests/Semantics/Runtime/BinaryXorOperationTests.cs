using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Semantics.Runtime.Abstract;
using RDCore.SDK.Semantics.Runtime.Operators;

namespace RDCore.Tests.Semantics.Runtime;

[TestClass]
[TestCategory("MS-VBAL 5.6.9.8.4 Binary 'Xor' Operator")]
public class BinaryXorOperationTests : BinaryOperatorOperationTests
{
    protected override BinaryOperatorSymbol Symbol => GlobalSymbols.OperatorSymbols.BitwiseXOrOp;
    internal override IRuntimeSemantics Semantics => new BinaryXorLogicalOperatorRuntimeSemantics();
    internal override IEnumerable<VBType> EffectiveTypes => [
        VBBooleanType.TypeInfo,
        VBByteType.TypeInfo,
        VBIntegerType.TypeInfo,
        VBLongType.TypeInfo,
        VBLongLongType.TypeInfo,
        VBNullType.TypeInfo,
    ];

    [TestMethod]
    [DataRow(0, 0, 0)]  
    [DataRow(0, -1, -1)]
    [DataRow(-1, 0, -1)]
    [DataRow(-1, -1, 0)]
    public void Operator_BitwiseContext_EvaluatesOp(object lhs, object rhs, int expected)
    {
        var context = CreateContext();
        var actual = EvaluateBinaryOp(context, lhs, rhs) as VBIntegerValue;
        Assert.AreEqual(expected, actual?.ManagedValue);
    }

    [TestMethod]
    [DataRow(5, 3, 6)]  
    [DataRow(12, 10, 6)]
    [DataRow(15, 0, 15)]
    [DataRow(15, 15, 0)]
    public void Operator_IntegerBitwise_EvaluatesOp(object lhs, object rhs, int expected)
    {
        var context = CreateContext();
        var actual = EvaluateBinaryOp(context, lhs, rhs) as VBIntegerValue;
        Assert.AreEqual(expected, actual?.ManagedValue);
    }
}
