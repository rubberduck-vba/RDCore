using NSubstitute;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.Runtime.Semantics.Operators;
using RDCore.Runtime.Semantics.Operators.Arithmetic;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Analysis;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Semantics.Runtime.Operators;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// What every operator's analysis does, whatever the operator (MS-VBAL 5.6.9): determine the effective type, analyze the
/// coercion of each operand to it, evaluate the operation the way it runs, and report what stopped it. Runs the real
/// operators over the real let-coercion analysis, through <c>Analyze</c>; the family-specific flags are in their own tests.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.6.9 Operators (analysis)")]
public sealed class OperatorAnalysisPipelineTests : LetCoercionRuntimeSemanticsTests
{
    private static BinaryAdditionOperatorRuntimeSemantics Add(ILetCoercionRuntimeSemanticsProvider? provider = null)
        => new(provider ?? LetCoercionAnalysisHarness.BuildProvider(), Formatter());

    private static ArithmeticOperatorSemanticFlags FlagsOf(BinaryAdditionOperatorRuntimeSemantics add, params VBTypedValue[] operands)
        => OperatorAnalysisHarness.Analyze(add, ThrowawayExpression, operands).Flags;

    #region operands of different types

    [TestMethod]
    public void Operands_OfDifferentTypes_AreAnalyzedAsTheyWouldBeEvaluated_NotAsTheyAre()
        // the operation is a Double one: it is analyzed on the operands as coerced to Double, not on a Long and a Double.
        => Assert.AreEqual(ArithmeticOperatorSemanticFlags.VBNumericEffectiveType, FlagsOf(Add(), new VBLongValue(1), new VBDoubleValue(2)));

    [TestMethod]
    public void ANullOperand_DoesNotStopTheAnalysis()
        => Assert.AreEqual(ArithmeticOperatorSemanticFlags.VBNullEffectiveType, FlagsOf(Add(), VBNullValue.Null, new VBLongValue(2)));

    [TestMethod]
    public void TwoNullOperands_AreAnalyzed()
        => Assert.AreEqual(ArithmeticOperatorSemanticFlags.VBNullEffectiveType, FlagsOf(Add(), VBNullValue.Null, VBNullValue.Null));

    #endregion

    #region which coercions are analyzed

    private static (RecordingLetCoercionProvider Provider, BinaryAdditionOperatorRuntimeSemantics Add) Recording()
    {
        var provider = new RecordingLetCoercionProvider(LetCoercionAnalysisHarness.BuildProvider());
        return (provider, Add(provider));
    }

    [TestMethod]
    public void AnOperandThatAlreadyHasTheEffectiveType_IsNotCoerced_SoNoCoercionIsAnalyzed()
    {
        var (provider, add) = Recording();

        FlagsOf(add, new VBLongValue(1), new VBLongValue(2));

        Assert.IsEmpty(provider.AnalyzedFrames);
    }

    [TestMethod]
    public void OnlyTheOperandThatIsCoerced_HasItsCoercionAnalyzed_ToTheEffectiveType()
    {
        var (provider, add) = Recording();

        FlagsOf(add, new VBLongValue(1), new VBDoubleValue(2));

        var frame = Assert.ContainsSingle(provider.AnalyzedFrames);
        Assert.AreEqual(InputIndex.BinaryLeftOperand, frame.OperandIndex);
        Assert.IsInstanceOfType<VBDoubleType>(frame.DestinationTypeDesc.Target);
    }

    [TestMethod]
    public void EachCoercedOperand_IsAnalyzedAsItsOwnOperand()
    {
        var (provider, add) = Recording();

        FlagsOf(add, new VBByteValue(1), new VBLongValue(2));

        var frame = Assert.ContainsSingle(provider.AnalyzedFrames);
        Assert.AreEqual(InputIndex.BinaryLeftOperand, frame.OperandIndex);
        Assert.IsInstanceOfType<VBLongType>(frame.DestinationTypeDesc.Target);
    }

    [TestMethod]
    public void ADateOperation_IsComputedInDouble_SoEveryOperandIsAnalyzedAgainstDouble()
        // MS-VBAL 5.6.9.3: a Date effective type is computed in Double - even the Date operand is coerced to it.
    {
        var (provider, add) = Recording();

        FlagsOf(add, new VBDateValue(1), new VBLongValue(2));

        Assert.HasCount(2, provider.AnalyzedFrames);
        Assert.IsTrue(provider.AnalyzedFrames.All(frame => frame.DestinationTypeDesc.Target is VBDoubleType));
        CollectionAssert.AreEquivalent(
            new[] { InputIndex.BinaryLeftOperand, InputIndex.BinaryRightOperand }, provider.AnalyzedFrames.Select(frame => frame.OperandIndex).ToArray());
    }

    [TestMethod]
    public void ANullOperation_CoercesNothing_SoNothingIsAnalyzed()
    {
        var (provider, add) = Recording();

        FlagsOf(add, VBNullValue.Null, new VBLongValue(2));

        Assert.IsEmpty(provider.AnalyzedFrames);
    }

    [TestMethod]
    public void WithoutAnEffectiveType_ThereIsNothingToCoerceTo_SoNothingIsAnalyzed()
    {
        var (provider, add) = Recording();

        // a String against an Object: no effective type exists.
        OperatorAnalysisHarness.Analyze(add, ThrowawayExpression, new VBStringValue("x"), new VBObjectValue(new RDCore.SDK.Model.Values.Bindings.ValueBindingHandle(new RDCore.SDK.Model.Values.Runtime.VBRuntimeValue<int>(1))));

        Assert.IsEmpty(provider.AnalyzedFrames);
    }

    #endregion

    #region the error that stopped the operation

    private static int? AnalysisErrorId(BinaryAdditionOperatorRuntimeSemantics add, params VBTypedValue[] operands)
        => OperatorAnalysisHarness.Analyze(add, ThrowawayExpression, operands).Errors.SingleOrDefault()?.ErrorId;

    [TestMethod]
    public void ASuccessfulOperation_ReportsNoError()
        => Assert.IsNull(AnalysisErrorId(Add(), new VBLongValue(1), new VBLongValue(2)));

    [TestMethod]
    public void AnOverflow_IsReportedAsTheOperationsError()
        => Assert.AreEqual((int)VBRuntimeErrorId.Overflow, AnalysisErrorId(Add(), new VBByteValue(200), new VBByteValue(100)));

    [TestMethod]
    public void AnOperandThatCannotBeCoerced_IsReportedAsATypeMismatch()
        => Assert.AreEqual((int)VBRuntimeErrorId.TypeMismatch, AnalysisErrorId(Add(), new VBStringValue("abc"), new VBLongValue(1)));

    [TestMethod]
    public void ADivisionByZero_IsReported()
        => Assert.AreEqual((int)VBRuntimeErrorId.DivisionByZero,
            OperatorAnalysisHarness.Analyze(new BinaryDivisionOperatorRuntimeSemantics(LetCoercionAnalysisHarness.BuildProvider(), Formatter()), ThrowawayExpression,
                new VBDoubleValue(5), new VBDoubleValue(0)).Errors.Single().ErrorId);

    [TestMethod]
    public void TheErrorReported_IsTheOneTheOperationRaises()
        // analysis describes the operation; it does not have a view of its own of what goes wrong.
    {
        var add = Add();
        (VBTypedValue Left, VBTypedValue Right)[] operations =
        [
            (new VBByteValue(200), new VBByteValue(100)),
            (new VBStringValue("abc"), new VBLongValue(1)),
            (new VBLongValue(1), new VBLongValue(2)),
            (new VBDoubleValue(1.5), new VBIntegerValue(2)),
            (VBNullValue.Null, new VBLongValue(2)),
        ];

        foreach (var (left, right) in operations)
        {
            var raised = OperatorAnalysisHarness.Evaluate(add, ThrowawayExpression, left, right).ErrorInfo?.ErrorId;

            Assert.AreEqual(raised, AnalysisErrorId(add, left, right), $"{left.TypeInfo.Name} + {right.TypeInfo.Name}");
        }
    }

    [TestMethod]
    public void TheError_IsReportedOnce()
    {
        var context = OperatorAnalysisHarness.Analyze(Add(), ThrowawayExpression, new VBStringValue("abc"), new VBLongValue(1));

        Assert.HasCount(1, context.Errors);
    }

    [TestMethod]
    public void AUnaryOperationsError_IsReportedOnce_Too()
    {
        var unary = new VBUnaryOperatorExpressionNode("-", NodeId, TestLocations.TestLocation,
            [new RDCore.SDK.Model.AST.Expressions.LiteralExpressionNode(default, TestLocations.TestLocation, new VBIntegerValue((short)0))]);

        var context = OperatorAnalysisHarness.Analyze(new UnaryNegationOperatorRuntimeSemantics(LetCoercionAnalysisHarness.BuildProvider(), Formatter()), unary, new VBByteValue(0));

        Assert.IsEmpty(context.Errors);
        var mismatch = OperatorAnalysisHarness.Analyze(new UnaryNegationOperatorRuntimeSemantics(LetCoercionAnalysisHarness.BuildProvider(), Formatter()), unary, new VBStringValue("abc"));
        Assert.HasCount(1, mismatch.Errors);
    }

    #endregion

    #region merging the analyses of an operation's operands

    private static LetCoercionAnalysisContext ContextOf(LetCoercionResult result, ConversionSemanticFlags flags)
        => new(NodeId, result, flags);

    private static LetCoercionStackFrame FrameFor(InputIndex operand)
        => new(NodeId, operand, new VBLongValue(1), new RDCore.SDK.Model.Values.Meta.VBTypeDescValue(VBDoubleType.TypeInfo));

    [TestMethod]
    public void Merge_CombinesTheFlagsOfBothOperands()
    {
        var left = ContextOf(LetCoercionResult.NotApplicable(FrameFor(InputIndex.BinaryLeftOperand)), ConversionSemanticFlags.Widening);
        var right = ContextOf(LetCoercionResult.NotApplicable(FrameFor(InputIndex.BinaryRightOperand)), ConversionSemanticFlags.Narrowing);

        Assert.AreEqual(ConversionSemanticFlags.Widening | ConversionSemanticFlags.Narrowing, left.Merge(right).Flags);
    }

    [TestMethod]
    public void Merge_KeepsTheFramesOfBothOperands_InOrder()
    {
        var leftFrame = FrameFor(InputIndex.BinaryLeftOperand);
        var rightFrame = FrameFor(InputIndex.BinaryRightOperand);

        var merged = ContextOf(LetCoercionResult.NotApplicable(leftFrame), 0).Merge(ContextOf(LetCoercionResult.NotApplicable(rightFrame), 0));

        Assert.AreEqual(2, merged.Result.Frames.Length);
        Assert.AreEqual(InputIndex.BinaryLeftOperand, merged.Result.Frames[0].OperandIndex);
        Assert.AreEqual(InputIndex.BinaryRightOperand, merged.Result.Frames[1].OperandIndex);
    }

    [TestMethod]
    public void Merge_AnOperandThatIsNotCoerced_HasNoFrame_AndContributesNone()
        // a Null operand, or one that already has the effective type, is not coerced: its result has no frames.
    {
        var coerced = ContextOf(LetCoercionResult.NotApplicable(FrameFor(InputIndex.BinaryLeftOperand)), ConversionSemanticFlags.Widening);
        var notCoerced = ContextOf(LetCoercionResult.Success(VBNullValue.Null, []), 0);

        Assert.AreEqual(1, coerced.Merge(notCoerced).Result.Frames.Length);
        Assert.AreEqual(1, notCoerced.Merge(coerced).Result.Frames.Length);
    }

    [TestMethod]
    public void Merge_TwoOperandsThatAreNotCoerced_HaveNoFrames()
        => Assert.IsTrue(ContextOf(LetCoercionResult.Success(VBNullValue.Null, []), 0)
            .Merge(ContextOf(LetCoercionResult.Success(VBNullValue.Null, []), 0)).Result.Frames.IsEmpty);

    [TestMethod]
    public void Merge_TheFailureOfEitherOperand_IsTheMergedFailure()
    {
        var error = VBRuntimeErrorInfo.For(VBRuntimeErrorId.Overflow, TestLocations.TestLocation, "overflow");
        var failed = ContextOf(LetCoercionResult.Error(error, FrameFor(InputIndex.BinaryLeftOperand)), ConversionSemanticFlags.Failed);
        var fine = ContextOf(LetCoercionResult.NotApplicable(FrameFor(InputIndex.BinaryRightOperand)), 0);

        Assert.AreSame(error, failed.Merge(fine).Result.ErrorInfo, "the left operand failed, and the right one coerced fine");
        Assert.AreSame(error, fine.Merge(failed).Result.ErrorInfo, "the right operand failed");
    }

    [TestMethod]
    public void Merge_WhenBothFail_TheFirstFailureIsTheMergedOne()
    {
        var first = VBRuntimeErrorInfo.For(VBRuntimeErrorId.Overflow, TestLocations.TestLocation, "first");
        var second = VBRuntimeErrorInfo.For(VBRuntimeErrorId.TypeMismatch, TestLocations.TestLocation, "second");

        var merged = ContextOf(LetCoercionResult.Error(first, FrameFor(InputIndex.BinaryLeftOperand)), 0)
            .Merge(ContextOf(LetCoercionResult.Error(second, FrameFor(InputIndex.BinaryRightOperand)), 0));

        Assert.AreSame(first, merged.Result.ErrorInfo);
    }

    #endregion
}
