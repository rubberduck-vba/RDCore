using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.Runtime.Semantics.Operators;
using RDCore.Runtime.Semantics.Operators.Arithmetic;
using RDCore.Runtime.Semantics.Operators.Logical;
using RDCore.Runtime.Semantics.Operators.Relational;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Context;
using RDCore.SDK.Semantics.Context.Abstract;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// What an operation does to each of its operands, and what is known about the operands, is in the semantic context of the operation:
/// the conversion semantic context of each operand.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.6.9 Operators (analysis)")]
public sealed class OperandConversionContextTests : LetCoercionRuntimeSemanticsTests
{
    private static ILetCoercionRuntimeSemanticsProvider Provider() => LetCoercionAnalysisHarness.BuildProvider();

    private static VBUnaryOperatorExpressionNode UnaryExpression()
        => new("-", NodeId, TestLocations.TestLocation, [new LiteralExpressionNode(default, TestLocations.TestLocation, new VBIntegerValue((short)0))]);

    private const ConversionSemanticFlags Implicitly = ConversionSemanticFlags.LetCoerced | ConversionSemanticFlags.Implicit;

    #region binary operators

    [TestMethod]
    public void ACoercedOperand_HasItsConversionsInItsOwnContext_TheOtherHasNone()
    {
        var context = OperatorAnalysisHarness.Analyze(new BinaryAdditionOperatorRuntimeSemantics(Provider(), Formatter()),
            ThrowawayExpression, new VBLongValue(1), new VBDoubleValue(2.5));

        // the Long is what is widened to the Double effective type; the Double is as it is.
        Assert.AreEqual(
            Implicitly | ConversionSemanticFlags.Numeric | ConversionSemanticFlags.CTypeAvailable | ConversionSemanticFlags.Widening | ConversionSemanticFlags.BinaryLeftOperand,
            context.LeftOperandConversionContext.Flags);
        Assert.AreEqual((ConversionSemanticFlags)0, context.RightOperandConversionContext.Flags);
    }

    [TestMethod]
    public void TheRightOperandIsItsOwn_NotTheLeftOnesAgain()
    {
        var context = OperatorAnalysisHarness.Analyze(new BinaryAdditionOperatorRuntimeSemantics(Provider(), Formatter()),
            ThrowawayExpression, new VBDoubleValue(2.5), new VBLongValue(1));

        Assert.AreEqual((ConversionSemanticFlags)0, context.LeftOperandConversionContext.Flags);
        Assert.IsTrue(context.RightOperandConversionContext.Flags.HasFlag(Implicitly | ConversionSemanticFlags.Widening | ConversionSemanticFlags.BinaryRightOperand));
        Assert.IsFalse(context.RightOperandConversionContext.Flags.HasFlag(ConversionSemanticFlags.BinaryLeftOperand));
    }

    [TestMethod]
    public void ANullOperand_IsNotCoerced_ButItsContextSaysItIsOne()
    {
        var context = OperatorAnalysisHarness.Analyze(new BinaryConcatOperatorRuntimeSemantics(Provider(), Formatter()),
            ThrowawayExpression, VBNullValue.Null, new VBLongValue(2));

        Assert.AreEqual(ConversionSemanticFlags.NullOperand | ConversionSemanticFlags.BinaryLeftOperand, context.LeftOperandConversionContext.Flags);
        Assert.IsTrue(context.RightOperandConversionContext.Flags.HasFlag(Implicitly | ConversionSemanticFlags.BinaryRightOperand));
    }

    [TestMethod]
    public void AnOperandThatIsCoercedByAnOperator_IsNeverExplicit()
    {
        var context = OperatorAnalysisHarness.Analyze(new BinaryAdditionOperatorRuntimeSemantics(Provider(), Formatter()),
            ThrowawayExpression, new VBLongValue(1), new VBDoubleValue(2.5));

        Assert.IsFalse(context.LeftOperandConversionContext.Flags.HasFlag(ConversionSemanticFlags.Explicit));
    }

    [TestMethod]
    public void Operands_OfTheSameType_HaveNothingToReport()
    {
        var context = OperatorAnalysisHarness.Analyze(new BinaryAdditionOperatorRuntimeSemantics(Provider(), Formatter()),
            ThrowawayExpression, new VBLongValue(1), new VBLongValue(2));

        Assert.IsEmpty(context.OperandConversionContexts);
        Assert.AreEqual((ConversionSemanticFlags)0, context.LeftOperandConversionContext.Flags);
        Assert.AreEqual((ConversionSemanticFlags)0, context.RightOperandConversionContext.Flags);
    }

    [TestMethod]
    public void ARelationalOperation_HasThemToo()
    {
        var context = OperatorAnalysisHarness.Analyze(new BinaryEqRelationalOperatorRuntimeSemantics(Provider(), Formatter()),
            ThrowawayExpression, new VBLongValue(1), new VBDoubleValue(2.5));

        Assert.IsTrue(context.LeftOperandConversionContext.Flags.HasFlag(Implicitly | ConversionSemanticFlags.Widening | ConversionSemanticFlags.BinaryLeftOperand));
    }

    [TestMethod]
    public void ALogicalOperation_HasThemToo()
    {
        var context = OperatorAnalysisHarness.Analyze(new BinaryAndLogicalOperatorRuntimeSemantics(Provider(), Formatter()),
            ThrowawayExpression, new VBByteValue(1), new VBLongValue(3));

        Assert.IsTrue(context.LeftOperandConversionContext.Flags.HasFlag(Implicitly | ConversionSemanticFlags.Widening | ConversionSemanticFlags.BinaryLeftOperand));
        Assert.AreEqual((ConversionSemanticFlags)0, context.RightOperandConversionContext.Flags);
    }

    [TestMethod]
    public void WithoutAnEffectiveType_NothingIsCoerced_SoThereIsNothingToReport()
    {
        var context = OperatorAnalysisHarness.Analyze(new BinaryAdditionOperatorRuntimeSemantics(Provider(), Formatter()),
            ThrowawayExpression, new VBObjectValue(new RDCore.SDK.Model.Values.Runtime.VBRuntimeObjectId()), new VBLongValue(1));

        Assert.IsEmpty(context.OperandConversionContexts);
    }

    #endregion

    #region unary operators

    [TestMethod]
    public void TheOperandOfAUnaryOperator_HasItsConversionsInItsContext()
    {
        var context = OperatorAnalysisHarness.Analyze(new UnaryNegationOperatorRuntimeSemantics(Provider(), Formatter()),
            UnaryExpression(), new VBStringValue("5"));

        Assert.IsTrue(context.UnaryOperandConversionContext.Flags.HasFlag(Implicitly | ConversionSemanticFlags.UnaryOperand));
    }

    [TestMethod]
    public void TheOperandOfAUnaryLogicalOperator_HasItsConversionsInItsContext()
    {
        var context = OperatorAnalysisHarness.Analyze(new UnaryNotOperatorRuntimeSemantics(Provider(), Formatter()),
            UnaryExpression(), new VBStringValue("5"));

        Assert.IsTrue(context.UnaryOperandConversionContext.Flags.HasFlag(Implicitly | ConversionSemanticFlags.UnaryOperand));
    }

    [TestMethod]
    public void AnOperandOfTheRightTypeOfAUnaryOperator_HasNothingToReport()
        => Assert.AreEqual((ConversionSemanticFlags)0, OperatorAnalysisHarness.Analyze(new UnaryNegationOperatorRuntimeSemantics(Provider(), Formatter()),
            UnaryExpression(), new VBDoubleValue(1.5)).UnaryOperandConversionContext.Flags);

    #endregion

    #region reading a context

    [TestMethod]
    public void TheContextOfAnOperand_OfAContextNothingAnalyzed_IsEmpty()
    {
        var context = new BinaryArithmeticOperatorSemanticContext();

        Assert.IsEmpty(context.OperandConversionContexts);
        Assert.AreEqual((ConversionSemanticFlags)0, context.ConversionContextOf(InputIndex.BinaryRightOperand).Flags);
        Assert.AreEqual((ConversionSemanticFlags)0, context.LeftOperandConversionContext.Flags);
    }

    #endregion
}
