using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Semantics.Runtime.Abstract;
using RDCore.SDK.Semantics.Runtime.Operators;

namespace RDCore.Tests.Semantics.Runtime;

[TestClass]
[TestCategory("MS-VBAL 5.6.9.8.6 Binary 'Imp' Operator")]
public class BinaryImpOperationTests : BinaryOperatorOperationTests
{
    internal override IRuntimeSemantics Semantics => new BinaryImpLogicalOperatorRuntimeSemantics(); protected override BinaryOperatorSymbol Symbol => GlobalSymbols.OperatorSymbols.BitwiseAndOp;
    internal override IEnumerable<VBType> EffectiveTypes => [
        VBBooleanType.TypeInfo,
        VBByteType.TypeInfo,
        VBIntegerType.TypeInfo,
        VBLongType.TypeInfo,
        VBLongLongType.TypeInfo,
        VBNullType.TypeInfo,
    ];

    [TestMethod]
    [DataRow(false, false, true)]
    [DataRow(false, true, true)] 
    [DataRow(true, false, false)]
    [DataRow(true, true, true)]  
    public void Operator_BooleanContext_EvaluatesOp(object lhs, object rhs, bool expected)
    {
        var context = CreateContext();
        var actual = EvaluateBinaryOp(context, lhs, rhs) as VBIntegerValue;
        var expectedValue = expected ? -1 : 0;
        Assert.AreEqual(expectedValue, (int?)actual?.Value);
    }

    [TestMethod]
    [DataRow(0, 0, -1)]
    [DataRow(0, -1, -1)]
    [DataRow(-1, 0, 0)] 
    [DataRow(-1, -1, -1)]
    public void Operator_BitwiseContext_EvaluatesOp(int lhs, int rhs, int expected)
    {
        var context = CreateContext();
        var actual = EvaluateBinaryOp(context, lhs, rhs) as VBIntegerValue;
        Assert.AreEqual(expected, (int?)actual?.Value);
    }

    [TestMethod]
    [DataRow(1, 2, -2)]
    [DataRow(5, 3, -5)]
    [DataRow(0, 0, -1)]
    public void Operator_IntegerBitwise_EvaluatesOp(int lhs, int rhs, int expected)
    {
        var context = CreateContext();
        var actual = EvaluateBinaryOp(context, lhs, rhs) as VBIntegerValue;
        Assert.AreEqual(expected, (int?)actual?.Value);
    }
}
