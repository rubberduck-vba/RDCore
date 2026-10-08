using RDCore.Runtime.Execution;
using RDCore.Runtime.Semantics;
using RDCore.Runtime.Semantics.Operators;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Facts;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// What the runtime semantics state about the conversions and operations they evaluate: the facts an observer is told of, at the point
/// where the evaluation happens.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.0.3 Semantic Analysis")]
public sealed class AnalysisObserverFactsTests : LetCoercionRuntimeSemanticsTests
{
    private sealed class NoSymbols : ISymbolProvider
    {
        public IEnumerable<Symbol> ProvideSymbols() => [];
    }

    private static readonly IRuntimeSession Session = RuntimeSessionComposer.Compose(
        new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, SupportsOptionCompareDatabase: true, OptionCompare.Text), new NoSymbols());

    // a provider owns its coercion stack: one per test, as tests run in parallel.
    private static IOperatorRuntimeSemanticsProvider Operators(IAnalysisObserver observer)
    {
        var observation = new AnalysisObservation(observer);
        return new OperatorRuntimeSemanticsProvider(LetCoercionAnalysisHarness.BuildProvider(observation), Formatter(), observation);
    }

    private static LiteralExpressionNode Operand() => new(NodeId, TestLocations.TestLocation, new VBLongValue(1));

    private static VBBinaryOperatorExpressionNode BinaryExpression(string token)
        => new(token, NodeId, TestLocations.TestLocation, Operand(), Operand());

    private static VBUnaryOperatorExpressionNode UnaryExpression(string token)
        => new(token, NodeId, TestLocations.TestLocation, [Operand()]);

    private static RecordingAnalysisObserver Observe(string token, VBTypedValue left, VBTypedValue right, ConversionSite site = ConversionSite.OperatorOperand)
    {
        var observer = new RecordingAnalysisObserver();
        Operators(observer).EvaluateBinaryOperator(Session, token, BinaryExpression(token), left, right, site);
        return observer;
    }

    private static RecordingAnalysisObserver Coerce(VBTypedValue source, VBType destination, ConversionSite site = ConversionSite.Assignment)
    {
        var observer = new RecordingAnalysisObserver();
        LetCoercionAnalysisHarness.BuildProvider(new AnalysisObservation(observer)).EvaluateLetCoercionSemantics(Session.Symbols.Resolver, ThrowawayExpression,
            new LetCoercionStackFrame(NodeId, InputIndex.CoercionSourceValue, source, new VBTypeDescValue(destination), site));
        return observer;
    }

    private static VBTypedValue Unknown(VBType type) => type.CreateIndeterminateValue();

    [TestMethod]
    public void ALetCoercion_IsStatedOnce_WithItsTypesItsSiteAndWhatItDoes()
    {
        var fact = Coerce(new VBDoubleValue(2.5), VBLongType.TypeInfo, ConversionSite.Assignment).Conversions.Single();

        Assert.AreEqual(ConversionSite.Assignment, fact.Site);
        Assert.AreEqual(VBDoubleType.TypeInfo, fact.Source);
        Assert.AreEqual(VBLongType.TypeInfo, fact.Destination);
        Assert.AreEqual(InputIndex.CoercionSourceValue, fact.Operand);
        Assert.IsTrue(fact.IsValueKnown);
        Assert.IsNull(fact.Error);
        Assert.AreEqual(
            ConversionSemanticFlags.Narrowing | ConversionSemanticFlags.Lossy | ConversionSemanticFlags.BankersRounding | ConversionSemanticFlags.Implicit,
            fact.Flags & (ConversionSemanticFlags.Narrowing | ConversionSemanticFlags.Lossy | ConversionSemanticFlags.BankersRounding | ConversionSemanticFlags.Implicit));
    }

    [TestMethod]
    public void ALetCoercionThatWidens_IsStatedAsWidening()
    {
        var fact = Coerce(new VBIntegerValue(1), VBLongType.TypeInfo).Conversions.Single();

        Assert.IsTrue(fact.Flags.HasFlag(ConversionSemanticFlags.Widening));
        Assert.IsFalse(fact.Flags.HasFlag(ConversionSemanticFlags.Narrowing));
    }

    [TestMethod]
    public void ALetCoercionNoOperationAsksToBeExplicit_IsImplicit()
        => Assert.IsTrue(Coerce(new VBDoubleValue(2.5), VBLongType.TypeInfo).Conversions.Single().Flags.HasFlag(ConversionSemanticFlags.Implicit));

    [TestMethod]
    public void ALetCoercionThatOverflows_OfAKnownValue_StatesTheOverflow()
    {
        var fact = Coerce(new VBDoubleValue(300), VBByteType.TypeInfo).Conversions.Single();

        Assert.IsTrue(fact.IsValueKnown);
        Assert.AreEqual((int)VBRuntimeErrorId.Overflow, fact.Error?.ErrorId);
    }

    [TestMethod]
    public void ALetCoercion_OfAnIndeterminateValue_StatesWhatTheTypesSay_ButNoError()
    {
        var fact = Coerce(Unknown(VBDoubleType.TypeInfo), VBByteType.TypeInfo).Conversions.Single();

        Assert.IsFalse(fact.IsValueKnown);
        Assert.IsNull(fact.Error);
        Assert.IsTrue(fact.Flags.HasFlag(ConversionSemanticFlags.Narrowing | ConversionSemanticFlags.Lossy | ConversionSemanticFlags.BankersRounding));
    }

    [TestMethod]
    public void TheAnalysisOfALetCoercion_StatesNoFact_ForItIsNotTheCodesConversion()
    {
        var observer = new RecordingAnalysisObserver();
        var frame = new LetCoercionStackFrame(NodeId, InputIndex.CoercionSourceValue, new VBDoubleValue(2.5), new VBTypeDescValue(VBLongType.TypeInfo));

        LetCoercionAnalysisHarness.BuildProvider(new AnalysisObservation(observer))
            .Analyze(Session.Symbols.Resolver, new LetCoercionSemanticContextFlagsBuilder(), ThrowawayExpression, frame);

        Assert.IsEmpty(observer.Conversions);
    }

    [TestMethod]
    public void AnObservationSuspended_HoldsItsFactsBack_UntilTheLastSuspensionEnds()
    {
        var observer = new RecordingAnalysisObserver();
        var observation = new AnalysisObservation(observer);
        var fact = Coerce(new VBDoubleValue(2.5), VBLongType.TypeInfo).Conversions.Single();

        using (observation.Suspend())
        {
            using (observation.Suspend())
            {
                observation.OnConversion(fact);
            }

            observation.OnConversion(fact);
            Assert.IsTrue(observation.IsSuspended);
        }

        Assert.IsFalse(observation.IsSuspended);
        observation.OnConversion(fact);
        Assert.HasCount(1, observer.Conversions);
    }

    [TestMethod]
    public void ACoercionThatCoercesOtherValuesToDescribeItself_StatesOnlyItsOwnFact()
    {
        // a Variant holding a Double, to Long: the strategy unwraps the Variant and coerces what it holds.
        var observer = Coerce(new VBVariantValue(new VBDoubleValue(2.5)), VBLongType.TypeInfo);

        Assert.HasCount(1, observer.Conversions);
    }

    [TestMethod]
    public void ALetCoercionWithoutASite_IsStatedUnspecified_NeverGuessed()
        => Assert.AreEqual(ConversionSite.Unspecified, Coerce(new VBDoubleValue(2.5), VBLongType.TypeInfo, ConversionSite.Unspecified).Conversions.Single().Site);

    [TestMethod]
    public void AnArithmeticOperation_IsStatedAsAnArithmeticFact_OfItsOperatorAndEffectiveType()
    {
        var observer = Observe(Tokens.AdditionOp, new VBLongValue(1), new VBLongValue(2));

        var fact = (ArithmeticOperatorFact)observer.Operations.Single();
        Assert.AreEqual(Tokens.AdditionOp, fact.Operator);
        Assert.AreEqual(VBLongType.TypeInfo, fact.EffectiveType);
        Assert.IsTrue(fact.IsValueKnown);
        Assert.IsNull(fact.Error);
        Assert.IsTrue(fact.Flags.HasFlag(RDCore.SDK.Semantics.Runtime.Operators.ArithmeticOperatorSemanticFlags.VBNumericEffectiveType));
        Assert.IsEmpty(observer.Conversions, "both operands are of the effective type: nothing is converted");
    }

    [TestMethod]
    public void ADivisionByAKnownZero_StatesTheDivisionByZero()
    {
        var fact = Observe(Tokens.DivisionOp, new VBLongValue(1), new VBLongValue(0)).Operations.Single();

        Assert.AreEqual((int)VBRuntimeErrorId.DivisionByZero, fact.Error?.ErrorId);
        Assert.IsTrue(fact.IsValueKnown);
    }

    [TestMethod]
    public void ADivisionByAnIndeterminateDivisor_StatesNoError_ForTheDivisorIsAssumedZero()
    {
        var fact = Observe(Tokens.DivisionOp, new VBLongValue(1), Unknown(VBLongType.TypeInfo)).Operations.Single();

        Assert.IsNull(fact.Error);
        Assert.IsFalse(fact.IsValueKnown);
        Assert.AreEqual(VBDoubleType.TypeInfo, fact.EffectiveType);
    }

    [TestMethod]
    public void AnOperandOfAnotherTypeThanTheEffectiveType_IsAConversionFactOfItsOwn_AtTheOperatorOperandSite()
    {
        var observer = Observe(Tokens.AdditionOp, new VBLongValue(1), new VBDoubleValue(2.5));

        Assert.HasCount(1, observer.Conversions, observer.Describe());
        var conversion = observer.Conversions.Single();
        Assert.AreEqual(ConversionSite.OperatorOperand, conversion.Site);
        Assert.AreEqual(InputIndex.BinaryLeftOperand, conversion.Operand);
        Assert.AreEqual(VBLongType.TypeInfo, conversion.Source);
        Assert.AreEqual(VBDoubleType.TypeInfo, conversion.Destination);
        Assert.IsTrue(conversion.Flags.HasFlag(ConversionSemanticFlags.Widening));
        Assert.AreEqual(VBDoubleType.TypeInfo, observer.Operations.Single().EffectiveType);
    }

    [TestMethod]
    public void TheSiteAnOperationIsEvaluatedFor_IsTheSiteOfTheConversionsOfItsOperands()
    {
        var observer = Observe(Tokens.CompareEqualOp, new VBLongValue(1), new VBDoubleValue(2.5), ConversionSite.CaseTest);

        Assert.IsNotEmpty(observer.Conversions);
        Assert.IsTrue(observer.Conversions.All(conversion => conversion.Site == ConversionSite.CaseTest));
    }

    [TestMethod]
    public void ARelationalOperation_IsStatedAsAComparisonFact_WithHowStringsAreCompared()
    {
        var fact = (ComparisonOperatorFact)Observe(Tokens.CompareEqualOp, new VBStringValue("a"), new VBStringValue("A")).Operations.Single();

        Assert.AreEqual(Tokens.CompareEqualOp, fact.Operator);
        Assert.AreEqual(Session.CurrentStringComparison(), fact.Comparison);
    }

    [TestMethod]
    public void ALogicalOperation_IsStatedAsALogicalFact()
        => Assert.IsInstanceOfType<LogicalOperatorFact>(Observe(Tokens.LogicalAndOp, new VBBooleanValue(true), new VBBooleanValue(false)).Operations.Single());

    [TestMethod]
    public void AConcatenation_IsStatedAsAConcatFact()
        => Assert.IsInstanceOfType<ConcatOperatorFact>(Observe(Tokens.ConcatOp, new VBStringValue("a"), new VBStringValue("b")).Operations.Single());

    [TestMethod]
    public void AUnaryArithmeticOperation_IsStatedAsAnArithmeticFact()
    {
        var observer = new RecordingAnalysisObserver();
        Operators(observer).EvaluateUnaryOperator(Session, UnaryExpression(Tokens.NegationOp), new VBLongValue(1));

        Assert.IsInstanceOfType<ArithmeticOperatorFact>(observer.Operations.Single());
    }

    [TestMethod]
    public void AUnaryLogicalOperation_IsStatedAsALogicalFact()
    {
        var observer = new RecordingAnalysisObserver();
        Operators(observer).EvaluateUnaryOperator(Session, UnaryExpression(Tokens.LogicalNotOp), new VBLongValue(1));

        Assert.IsInstanceOfType<LogicalOperatorFact>(observer.Operations.Single());
    }

    [TestMethod]
    public void AnOperationThatFails_StatesItsError_ButAnInternalErrorIsADefectAndNotAFactOfTheCode()
    {
        Assert.IsNull(RDCore.Runtime.Semantics.StatedErrors.Of(
            VBRuntimeErrorInfo.For(VBRuntimeErrorId.InternalError, TestLocations.TestLocation, "internal")));
        Assert.AreEqual((int)VBRuntimeErrorId.Overflow, RDCore.Runtime.Semantics.StatedErrors.Of(
            VBRuntimeErrorInfo.For(VBRuntimeErrorId.Overflow, TestLocations.TestLocation, "overflow"))?.ErrorId);
        Assert.IsNull(RDCore.Runtime.Semantics.StatedErrors.Of(null));
    }

    [TestMethod]
    public void AProviderWithoutAnObserver_EvaluatesAsItAlwaysDid()
    {
        var operators = new OperatorRuntimeSemanticsProvider(LetCoercionAnalysisHarness.BuildProvider(), Formatter());
        var result = operators.EvaluateBinaryOperator(Session, BinaryExpression(Tokens.AdditionOp), new VBLongValue(1), new VBLongValue(2));

        Assert.AreEqual(3, ((VBLongValue)result.Result!).Value);
    }
}
