using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Semantics.Runtime.Abstract;
using RDCore.SDK.Semantics.Runtime.Operators;

namespace RDCore.Tests.Semantics.Runtime;

[TestClass]
[TestCategory("MS-VBAL 5.6.9.8.5 Binary 'Eqv' Operator")]
public class BinaryEqvOperationTests : BinaryOperatorOperationTests
{
    protected override BinaryOperatorSymbol Symbol => GlobalSymbols.OperatorSymbols.BitwiseEqvOp;
    internal override IRuntimeSemantics Semantics => new BinaryEqvBitwiseOperatorRuntimeSemantics();
    internal override IEnumerable<VBType> EffectiveTypes => [
        VBBooleanType.TypeInfo,
        VBByteType.TypeInfo,
        VBIntegerType.TypeInfo,
        VBLongType.TypeInfo,
        VBLongLongType.TypeInfo,
        VBNullType.TypeInfo,
    ];

    [TestMethod]
    [DataRow(0, 0, -1)] 
    [DataRow(0, -1, 0)] 
    [DataRow(-1, 0, 0)] 
    [DataRow(-1, -1, -1)]
    public void Operator_BitwiseContext_EvaluatesOp(object lhs, object rhs, int expected)
    {
        var context = CreateContext();
        var actual = EvaluateBinaryOp(context, lhs, rhs) as VBIntegerValue;
        Assert.AreEqual(expected, actual?.ManagedValue);
    }

    [TestMethod]
    [DataRow(5, 3, -7)]  
    [DataRow(12, 10, -7)]
    [DataRow(15, 0, -16)]
    [DataRow(15, 15, -1)]
    public void Operator_IntegerBitwise_EvaluatesOp(object lhs, object rhs, int expected)
    {
        var context = CreateContext();
        var actual = EvaluateBinaryOp(context, lhs, rhs) as VBIntegerValue;
        Assert.AreEqual(expected, actual?.ManagedValue);
    }
}
