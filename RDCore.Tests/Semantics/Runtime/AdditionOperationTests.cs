using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Semantics.Runtime.Abstract;
using RDCore.SDK.Semantics.Runtime.Operators;
using System.Data;

namespace RDCore.Tests.Semantics.Runtime;

[TestClass]
[TestCategory("MS-VBAL 5.6.9.3.2 Binary '+' Operator")]
public class AdditionOperationTests : BinaryOperatorOperationTests
{
    protected override BinaryOperatorSymbol Symbol => GlobalSymbols.OperatorSymbols.AdditionOp;
    internal override IRuntimeSemantics Semantics => new BinaryAdditionOperatorRuntimeSemantics();

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
        VBStringType.TypeInfo,
        VBNullType.TypeInfo,
    ];

    [TestMethod]
    [DataRow(0, 0, 0)]
    [DataRow(0, 1, 1)]
    [DataRow(1, 0, 1)]
    [DataRow(1, 1, 2)]
    [DataRow(1, 2, 3)]
    [DataRow(-2, 0, -2)]
    [DataRow(-2, 2, 0)]
    [DataRow(-2, -2, -4)]
    [DataRow(2, 0, 2)]
    [DataRow(2, -2, 0)]
    [DataRow(0, -2, -2)]
    public void Operator_CalculatesResult(object lhs, object rhs, object expected)
    {
        var actual = EvaluateBinaryOp(CreateContext(), lhs, rhs) as INumericValue;
        Assert.AreEqual(Convert.ToDouble(expected), actual?.ManagedValue);
    }

    [TestMethod]
    public void EmptyOperands_ResultsIsIntegerZero()
    {
        var result = EvaluateBinaryOp(CreateContext(), "Empty", "Empty");

        Assert.IsInstanceOfType<VBIntegerValue>(result);
        Assert.AreEqual(0, ((VBIntegerValue)result).Value);
    }
}
