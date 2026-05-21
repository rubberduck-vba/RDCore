using RDCore.SDK.Model;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Expressions.Operators;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Model;
using RDCore.SDK.Semantics.Runtime.Abstract;
using RDCore.SDK.Semantics.Runtime.Operators;

namespace RDCore.Tests.Semantics.Runtime;

[TestClass]
[TestCategory("MS-VBAL 5.6.9.3.2 Binary '+' Operator")]
public class AdditionOperationTests : SymbolOperationTests
{
    internal override RuntimeSemantics Semantics => new BinaryAdditionOperatorRuntimeSemantics();
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
    [DataRow(2, 2, 4)]
    [DataRow(-2, 2, 0)]
    [DataRow(-2, -2, -4)]
    [DataRow(2, -2, 0)]
    [DataRow(0, 2, 2)]
    [DataRow(2, 0, 2)]
    [DataRow(0, 0, 0)]
    public void EvaluateAddition_HappyPath_CalculatesResult(object lhs, object rhs, object expected)
    {
        var actual = EvaluateAddition(CreateContext(), lhs, rhs) as INumericValue;
        Assert.AreEqual(Convert.ToDouble(expected), actual?.ManagedValue);
    }

    [TestMethod]
    [DataRow("1.5", 1, 2.5d)]         // String + Integer -> Double
    [DataRow("1.5", "1", "1.51")]         // String + String -> String
    [DataRow(32767, 1.0, 32768.0d)]   // Integer + Double -> Double (Safe)
    public void EvaluateAddition_NumericCoercion(object lhs, object rhs, object expected)
    {
        var result = EvaluateAddition(CreateContext(), lhs, rhs);
        if (expected is not string)
        {
            Assert.AreEqual(Convert.ToDouble(expected), ((INumericValue)result).ManagedValue, 0.0001);
        }
    }

    [TestMethod]
    public void EvaluateAddition_BothEmpty_ResultsIsIntegerZero()
    {
        var result = EvaluateAddition(CreateContext(), "Empty", "Empty");

        Assert.IsInstanceOfType<VBIntegerValue>(result);
        Assert.AreEqual(0, ((VBIntegerValue)result).Value);
    }

    private VBTypedValue EvaluateAddition(IVBExecutionContext context, object lhs, object rhs)
    {
        var lhsValue = WrapVBTypedValue(lhs, TestLocationLHS);
        var rhsValue = WrapVBTypedValue(rhs, TestLocationRHS);

        var lhsExpression = new LiteralExpression(TestLocationLHS, lhsValue);
        var rhsExpression = new LiteralExpression(TestLocationRHS, rhsValue);

        var expression = new VBBinaryOperatorExpression(GlobalSymbols.OperatorSymbols.Addition, lhsExpression, rhsExpression, TestLocation);

        if (lhsValue?.TypeInfo is null)
        {
            Assert.Inconclusive();
        }
        if (rhsValue?.TypeInfo is null)
        {
            Assert.Inconclusive();
        }

        return Semantics.Evaluate(context, expression, lhsValue, rhsValue)!;
    }
}
