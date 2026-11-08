using RDCore.SDK.Model.Errors;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Context;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Semantics.Runtime.Operators;
using RDCore.Runtime.LanguageCore.Errors;

namespace RDCore.Tests.Semantics.Builders;

/// <summary>
/// The builders every semantic analysis contributes to: flags accumulate, errors ride along, and <c>Build()</c> yields a
/// context carrying exactly what was contributed. Exercised through the conversion context (the let-coercion analysis'
/// own) and through the generic builder.
/// </summary>
[TestClass]
public sealed class SemanticContextBuilderTests
{
    private static VBRuntimeErrorInfo RuntimeError(VBRuntimeErrorId id = VBRuntimeErrorId.TypeMismatch)
        => VBRuntimeErrorInfo.For(id, TestLocations.TestLocation, "verbose");

    #region flags

    [TestMethod]
    public void Flags_OfAFreshBuilder_AreNone()
        => Assert.AreEqual((ConversionSemanticFlags)0, new LetCoercionSemanticContextFlagsBuilder().Flags);

    [TestMethod]
    public void AddFlags_CombinesEveryFlagAdded()
    {
        var builder = new LetCoercionSemanticContextFlagsBuilder();

        builder.AddFlags(ConversionSemanticFlags.Implicit);
        builder.AddFlags(ConversionSemanticFlags.Narrowing | ConversionSemanticFlags.Lossy);

        Assert.AreEqual(ConversionSemanticFlags.Implicit | ConversionSemanticFlags.Narrowing | ConversionSemanticFlags.Lossy, builder.Flags);
    }

    [TestMethod]
    public void AddFlags_TheSameFlagTwice_IsIdempotent()
    {
        var builder = new LetCoercionSemanticContextFlagsBuilder();

        builder.AddFlags(ConversionSemanticFlags.Widening);
        builder.AddFlags(ConversionSemanticFlags.Widening);

        Assert.AreEqual(ConversionSemanticFlags.Widening, builder.Flags);
    }

    [TestMethod]
    public void AddFlags_ReturnsTheBuilder_SoContributionsChain()
    {
        var builder = new LetCoercionSemanticContextFlagsBuilder();

        Assert.AreSame(builder, builder.AddFlags(ConversionSemanticFlags.Implicit));
    }

    [TestMethod]
    public void Build_CarriesTheFlags()
    {
        var builder = new LetCoercionSemanticContextFlagsBuilder();
        builder.AddFlags(ConversionSemanticFlags.NullOperand | ConversionSemanticFlags.Failed);

        var context = builder.Build();

        Assert.AreEqual(ConversionSemanticFlags.NullOperand | ConversionSemanticFlags.Failed, context.Flags);
    }

    [TestMethod]
    public void Build_OfAFreshBuilder_HasNoFlagsOrErrors()
    {
        var context = new LetCoercionSemanticContextFlagsBuilder().Build();

        Assert.AreEqual((ConversionSemanticFlags)0, context.Flags);
        Assert.IsEmpty(context.Errors);
    }

    #endregion

    #region errors

    [TestMethod]
    public void Build_CarriesTheErrorsAdded_InTheOrderTheyWereAdded()
    {
        var first = RuntimeError(VBRuntimeErrorId.TypeMismatch);
        var second = RuntimeError(VBRuntimeErrorId.Overflow);
        var builder = new LetCoercionSemanticContextFlagsBuilder();
        builder.AddFlags(ConversionSemanticFlags.Failed);

        builder.AddOnError(first);
        builder.AddOnError(second);
        var context = builder.Build();

        Assert.HasCount(2, context.Errors);
        Assert.AreSame(first, context.Errors[0]);
        Assert.AreSame(second, context.Errors[1]);
    }

    [TestMethod]
    public void AddOnError_Null_IsIgnored()
    {
        var builder = new LetCoercionSemanticContextFlagsBuilder();

        builder.AddOnError<VBRuntimeErrorInfo>(null);

        Assert.IsEmpty(builder.Build().Errors);
    }

    #endregion

    #region let-coercion flags per operand

    [TestMethod]
    public void AddLetCoercionFlags_ForTheSameOperandTwice_DoesNotThrow()
    // the provider adds an operand's flags twice (the operation's, then the operand's own).
    {
        var builder = new LetCoercionSemanticContextFlagsBuilder();

        builder.AddLetCoercionFlags(ConversionSemanticFlags.Implicit, InputIndex.BinaryLeftOperand);
        builder.AddLetCoercionFlags(ConversionSemanticFlags.BinaryLeftOperand, InputIndex.BinaryLeftOperand);
    }

    [TestMethod]
    public void LetCoercionFlagsOf_AccumulatesPerOperand_AndKeepsOperandsApart()
    {
        var builder = new LetCoercionSemanticContextFlagsBuilder();

        builder.AddLetCoercionFlags(ConversionSemanticFlags.Widening, InputIndex.BinaryLeftOperand);
        builder.AddLetCoercionFlags(ConversionSemanticFlags.Numeric, InputIndex.BinaryLeftOperand);
        builder.AddLetCoercionFlags(ConversionSemanticFlags.Narrowing, InputIndex.BinaryRightOperand);

        Assert.AreEqual(ConversionSemanticFlags.Widening | ConversionSemanticFlags.Numeric, builder.LetCoercionFlagsOf(InputIndex.BinaryLeftOperand));
        Assert.AreEqual(ConversionSemanticFlags.Narrowing, builder.LetCoercionFlagsOf(InputIndex.BinaryRightOperand));
    }

    [TestMethod]
    public void LetCoercionFlagsOf_AnOperandNothingWasAddedFor_IsNone()
        => Assert.AreEqual((ConversionSemanticFlags)0, new LetCoercionSemanticContextFlagsBuilder().LetCoercionFlagsOf(InputIndex.UnaryOperand));

    [TestMethod]
    public void AddLetCoercionFlags_ReturnsTheBuilder_SoContributionsChain()
    {
        var builder = new LetCoercionSemanticContextFlagsBuilder();

        Assert.AreSame(builder, builder.AddLetCoercionFlags(ConversionSemanticFlags.Implicit, InputIndex.UnaryOperand));
    }

    [TestMethod]
    public void AddLetCoercionFlags_OnAGenericBuilder_AreKeptPerOperand_ButNotMixedIntoItsOwnFlags()
    // a builder for another kind of context (here still conversion-typed, but through the generic builder, which does
    // not fold them in) keeps the operand flags for whoever asks; its own flags stay its own.
    {
        var builder = new SemanticContextFlagsBuilder<ConversionOperationSemanticContext, ConversionSemanticFlags>();

        builder.AddLetCoercionFlags(ConversionSemanticFlags.Widening, InputIndex.BinaryLeftOperand);

        Assert.AreEqual(ConversionSemanticFlags.Widening, builder.LetCoercionFlagsOf(InputIndex.BinaryLeftOperand));
        Assert.AreEqual((ConversionSemanticFlags)0, builder.Flags);
    }

    [TestMethod]
    public void TheContextOfAnOperator_IsBuiltWithTheConversionContextOfEachOperand()
    {
        var builder = new SemanticContextFlagsBuilder<BinaryArithmeticOperatorSemanticContext, ArithmeticOperatorSemanticFlags>();

        builder.AddLetCoercionFlags(ConversionSemanticFlags.Widening, InputIndex.BinaryLeftOperand);
        builder.AddLetCoercionFlags(ConversionSemanticFlags.Narrowing, InputIndex.BinaryRightOperand);
        builder.AddLetCoercionFlags(ConversionSemanticFlags.Numeric, InputIndex.BinaryLeftOperand);

        var context = builder.Build();

        Assert.HasCount(2, context.OperandConversionContexts);
        Assert.AreEqual(ConversionSemanticFlags.Widening | ConversionSemanticFlags.Numeric, context.LeftOperandConversionContext.Flags);
        Assert.AreEqual(ConversionSemanticFlags.Narrowing, context.RightOperandConversionContext.Flags);
    }

    [TestMethod]
    public void AnOperandNothingWasAddedFor_HasAnEmptyConversionContext_EvenBeforeAnOperandThatHasOne()
    {
        var builder = new SemanticContextFlagsBuilder<BinaryArithmeticOperatorSemanticContext, ArithmeticOperatorSemanticFlags>();

        builder.AddLetCoercionFlags(ConversionSemanticFlags.Narrowing, InputIndex.BinaryRightOperand);

        var context = builder.Build();

        Assert.AreEqual((ConversionSemanticFlags)0, context.LeftOperandConversionContext.Flags);
        Assert.AreEqual(ConversionSemanticFlags.Narrowing, context.RightOperandConversionContext.Flags);
    }

    [TestMethod]
    public void TheContextOfAnOperator_NothingWasAddedFor_HasNoOperandConversionContexts()
        => Assert.IsEmpty(new SemanticContextFlagsBuilder<BinaryArithmeticOperatorSemanticContext, ArithmeticOperatorSemanticFlags>().Build().OperandConversionContexts);

    [TestMethod]
    public void AContextThatIsNotAnOperators_IsBuiltAsItIs_WhateverWasAddedForOperands()
    {
        var builder = new SemanticContextFlagsBuilder<ConversionOperationSemanticContext, ConversionSemanticFlags>();

        builder.AddLetCoercionFlags(ConversionSemanticFlags.Widening, InputIndex.BinaryLeftOperand);

        Assert.AreEqual((ConversionSemanticFlags)0, builder.Build().Flags);
    }

    [TestMethod]
    public void ConversionSemanticFlags_All_IsEveryFlagThereIs()
        // `All` had been left behind as flags were added.
        => Assert.AreEqual(ConversionSemanticFlags.All,
            Enum.GetValues<ConversionSemanticFlags>().Where(flag => flag != ConversionSemanticFlags.All).Aggregate((ConversionSemanticFlags)0, (all, flag) => all | flag));

    [TestMethod]
    public void AddLetCoercionFlags_ReachTheBuiltConversionContext()
    // for a conversion builder the operand flags ARE conversion flags: dropping them would leave the operation's
    // context blind to how its operands were coerced.
    {
        var builder = new LetCoercionSemanticContextFlagsBuilder();
        builder.AddLetCoercionFlags(ConversionSemanticFlags.Widening, InputIndex.BinaryLeftOperand);
        builder.AddLetCoercionFlags(ConversionSemanticFlags.Narrowing, InputIndex.BinaryRightOperand);

        var context = builder.Build();

        Assert.AreEqual(ConversionSemanticFlags.Widening | ConversionSemanticFlags.Narrowing, context.Flags);
    }

    #endregion
}
