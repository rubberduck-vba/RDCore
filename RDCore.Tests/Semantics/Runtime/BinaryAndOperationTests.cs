using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Semantics.Runtime.Abstract;
using RDCore.SDK.Semantics.Runtime.Operators;

namespace RDCore.Tests.Semantics.Runtime;

[TestClass]
[TestCategory("MS-VBAL 5.6.9.8.2 Binary 'And' Operator")]
public class BinaryAndOperationTests : BinaryOperatorOperationTests
{
    protected override BinaryOperatorSymbol Symbol => GlobalSymbols.OperatorSymbols.BitwiseAndOp;
    internal override IRuntimeSemantics Semantics => new BinaryAndBitwiseOperatorRuntimeSemantics();
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
    [DataRow(false, true, false)] 
    [DataRow(true, false, false)] 
    [DataRow(true, true, true)]   
    public void Operator_BooleanContext_EvaluatesOp(object lhs, object rhs, bool expected)
    {
        var context = CreateContext();
        var actual = EvaluateBinaryOp(context, lhs, rhs) as VBBooleanValue;
        Assert.AreEqual(expected, actual?.Value);
    }

    [TestMethod]
    [DataRow(0, 0, 0)]      
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
    [DataRow(5, 3, 1)]      
    [DataRow(12, 10, 8)]    
    [DataRow(15, 0, 0)]     
    public void Operator_IntegerBitwise_EvaluatesOp(object lhs, object rhs, int expected)
    {
        var context = CreateContext();
        var actual = EvaluateBinaryOp(context, lhs, rhs) as VBIntegerValue;
        Assert.AreEqual(expected, actual?.ManagedValue);
    }
}
