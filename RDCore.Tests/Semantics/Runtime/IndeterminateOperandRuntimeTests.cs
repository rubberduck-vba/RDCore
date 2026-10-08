using RDCore.Runtime.Execution;
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
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// An operation or a let-coercion of an indeterminate operand yields an indeterminate value, and raises nothing: an error it would raise for the value
/// the operand assumes is not known to happen. Known operands are evaluated as they always are.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.0.3 Semantic Analysis")]
public sealed class IndeterminateOperandRuntimeTests : LetCoercionRuntimeSemanticsTests
{
    private sealed class NoSymbols : ISymbolProvider
    {
        public IEnumerable<Symbol> ProvideSymbols() => [];
    }

    private static readonly IRuntimeSession Session = RuntimeSessionComposer.Compose(
        new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, SupportsOptionCompareDatabase: true, OptionCompare.Text), new NoSymbols());

    // a provider owns its coercion stack: one per operation, as tests run in parallel.
    private static IOperatorRuntimeSemanticsProvider Operators()
        => new OperatorRuntimeSemanticsProvider(LetCoercionAnalysisHarness.BuildProvider(), Formatter());

    private static LiteralExpressionNode Operand() => new(NodeId, TestLocations.TestLocation, new VBLongValue(1));

    private static RuntimeSemanticsEvaluationResult Binary(string token, VBTypedValue left, VBTypedValue right)
        => Operators().EvaluateBinaryOperator(Session, new VBBinaryOperatorExpressionNode(token, NodeId, TestLocations.TestLocation, Operand(), Operand()), left, right);

    private static RuntimeSemanticsEvaluationResult Unary(string token, VBTypedValue operand)
        => Operators().EvaluateUnaryOperator(Session, new VBUnaryOperatorExpressionNode(token, NodeId, TestLocations.TestLocation, [Operand()]), operand);

    private static LetCoercionResult Coerce(VBTypedValue source, VBType destination)
        => LetCoercionAnalysisHarness.BuildProvider().EvaluateLetCoercionSemantics(Session.Symbols.Resolver, ThrowawayExpression,
            new LetCoercionStackFrame(NodeId, InputIndex.CoercionSourceValue, source, new VBTypeDescValue(destination)));

    private static VBTypedValue Unknown(VBType type) => type.CreateIndeterminateValue();

    private static readonly string[] BinaryTokens =
    [
        Tokens.AdditionOp, Tokens.SubtractionOp, Tokens.MultiplicationOp, Tokens.DivisionOp, Tokens.IntegerDivisionOp, Tokens.ModuloOp,
        Tokens.PowerOp, Tokens.ConcatOp, Tokens.CompareIsOp, Tokens.CompareEqualOp, Tokens.CompareNotEqualOp, Tokens.CompareGreaterThanOp,
        Tokens.CompareGreaterThanOrEqualOp, Tokens.CompareLessThanOp, Tokens.CompareLessThanOrEqualOp, Tokens.CompareLikeOp,
        Tokens.LogicalAndOp, Tokens.LogicalOrOp, Tokens.LogicalXOrOp, Tokens.LogicalEqvOp, Tokens.LogicalImpOp,
    ];

    private static readonly VBType[] DeclaredTypes =
    [
        VBByteType.TypeInfo, VBBooleanType.TypeInfo, VBIntegerType.TypeInfo, VBLongType.TypeInfo, VBLongLongType.TypeInfo, VBSingleType.TypeInfo,
        VBDoubleType.TypeInfo, VBCurrencyType.TypeInfo, VBDecimalType.TypeInfo, VBDateType.TypeInfo, VBStringType.TypeInfo, VBVariantType.TypeInfo,
        VBObjectType.TypeInfo,
    ];

    [TestMethod]
    public void EveryBinaryOperator_OfIndeterminateOperandsOfEveryDeclaredType_YieldsAnIndeterminateValue()
    {
        var failures = new List<string>();
        foreach (var token in BinaryTokens)
        {
            foreach (var left in DeclaredTypes)
            {
                foreach (var right in DeclaredTypes)
                {
                    var result = Binary(token, Unknown(left), Unknown(right));
                    if (!result.IsSuccess || !result.Result!.IsIndeterminate)
                    {
                        failures.Add($"{left.Name} {token} {right.Name}: {(result.IsSuccess ? "a known value" : $"error {result.ErrorInfo?.ErrorId}")}");
                    }
                }
            }
        }

        Assert.IsEmpty(failures, string.Join(Environment.NewLine, failures));
    }

    [TestMethod]
    public void AnOperation_OfOneIndeterminateOperand_YieldsAnIndeterminateValue()
    {
        var result = Binary(Tokens.AdditionOp, new VBLongValue(1), Unknown(VBLongType.TypeInfo));

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(result.Result!.IsIndeterminate);
        Assert.IsInstanceOfType<VBLongValue>(result.Result);
    }

    [TestMethod]
    public void AnOperation_OfKnownOperands_YieldsAKnownValue()
    {
        var result = Binary(Tokens.AdditionOp, new VBLongValue(1), new VBLongValue(2));

        Assert.IsFalse(result.Result!.IsIndeterminate);
        Assert.AreEqual(3, ((VBLongValue)result.Result).Value);
    }

    [TestMethod]
    public void ADivisionByAKnownZero_IsADivisionByZero()
        => Assert.AreEqual((int)VBRuntimeErrorId.DivisionByZero, Binary(Tokens.DivisionOp, new VBLongValue(1), new VBLongValue(0)).ErrorInfo?.ErrorId);

    [TestMethod]
    public void ADivisionByAnIndeterminateDivisor_RaisesNothing_AndYieldsAnIndeterminateValueOfItsEffectiveType()
    {
        // the divisor assumes 0: the division by zero that would raise is not known to happen.
        var result = Binary(Tokens.DivisionOp, new VBLongValue(1), Unknown(VBLongType.TypeInfo));

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(result.Result!.IsIndeterminate);
        Assert.IsInstanceOfType<VBDoubleValue>(result.Result);
    }

    [TestMethod]
    public void AComparison_OfAnIndeterminateOperand_YieldsAnIndeterminateBoolean()
    {
        var result = Binary(Tokens.CompareEqualOp, Unknown(VBStringType.TypeInfo), new VBStringValue("abc"));

        Assert.IsTrue(result.Result!.IsIndeterminate);
        Assert.IsInstanceOfType<VBBooleanValue>(result.Result);
    }

    [TestMethod]
    public void AComparison_OfAnIndeterminateObject_RaisesNothing_AndYieldsAnIndeterminateVariant()
    {
        // an object assumes Nothing, which has no default member to compare; the value of its default member would decide the effective type,
        // and a comparison of Null is Null.
        var result = Binary(Tokens.CompareEqualOp, Unknown(VBObjectType.TypeInfo), new VBLongValue(1));

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(result.Result!.IsIndeterminate);
        Assert.IsInstanceOfType<VBVariantValue>(result.Result);
    }

    [TestMethod]
    public void AnOperation_OfAnIndeterminateVariant_YieldsAnIndeterminateVariant()
    {
        // the subtype of the Variant decides the effective type, and so the type of the result.
        var result = Binary(Tokens.AdditionOp, Unknown(VBVariantType.TypeInfo), new VBLongValue(1));

        Assert.IsTrue(result.Result!.IsIndeterminate);
        Assert.IsInstanceOfType<VBVariantValue>(result.Result);
    }

    [TestMethod]
    [DataRow(Tokens.NegationOp)]
    [DataRow(Tokens.LogicalNotOp)]
    public void AUnaryOperation_OfAnIndeterminateOperand_YieldsAnIndeterminateValue(string token)
    {
        var result = Unary(token, Unknown(VBIntegerType.TypeInfo));

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(result.Result!.IsIndeterminate);
    }

    [TestMethod]
    public void ALetCoercion_OfAnIndeterminateSource_YieldsAnIndeterminateValueOfTheDestinationType()
    {
        var result = Coerce(Unknown(VBDoubleType.TypeInfo), VBByteType.TypeInfo);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(result.Result!.IsIndeterminate);
        Assert.IsInstanceOfType<VBByteValue>(result.Result);
    }

    [TestMethod]
    public void ALetCoercion_ThatWouldRaiseForTheAssumedValue_RaisesNothing()
    {
        // a String assumes "", which is not a number: the type mismatch is not known to happen.
        Assert.AreEqual((int)VBRuntimeErrorId.TypeMismatch, Coerce(new VBStringValue(string.Empty), VBLongType.TypeInfo).ErrorInfo?.ErrorId);

        var result = Coerce(Unknown(VBStringType.TypeInfo), VBLongType.TypeInfo);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(result.Result!.IsIndeterminate);
        Assert.IsInstanceOfType<VBLongValue>(result.Result);
    }

    [TestMethod]
    [DataRow(nameof(VBVariantType))]
    [DataRow(nameof(VBObjectType))]
    public void ALetCoercion_OfAnIndeterminateVariantOrObject_YieldsAnIndeterminateValueOfTheDestinationType(string sourceType)
    {
        var source = sourceType == nameof(VBVariantType) ? Unknown(VBVariantType.TypeInfo) : Unknown(VBObjectType.TypeInfo);

        var result = Coerce(source, VBLongType.TypeInfo);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(result.Result!.IsIndeterminate);
        Assert.IsInstanceOfType<VBLongValue>(result.Result);
    }

    [TestMethod]
    public void ALetCoercion_OfAKnownSource_YieldsAKnownValue()
    {
        var result = Coerce(new VBDoubleValue(2.5), VBByteType.TypeInfo);

        Assert.IsFalse(result.Result!.IsIndeterminate);
        Assert.AreEqual((byte)2, ((VBByteValue)result.Result).Value);
    }

    [TestMethod]
    public void TheAnalysisOfALetCoercion_OfAnIndeterminateSource_StatesNoError_ButStatesWhatTheTypesSay()
    {
        var analysis = LetCoercionAnalysisHarness.Analyze(Unknown(VBDoubleType.TypeInfo), VBByteType.TypeInfo, ThrowawayExpression);

        Assert.IsEmpty(analysis.Builder.Build().Errors);
        Assert.IsTrue(analysis.Flags.HasFlag(ConversionSemanticFlags.Narrowing | ConversionSemanticFlags.Lossy | ConversionSemanticFlags.BankersRounding));
    }

    [TestMethod]
    public void TheAnalysisOfALetCoercion_OfAKnownSourceThatOverflows_StatesTheOverflow()
    {
        var analysis = LetCoercionAnalysisHarness.Analyze(new VBDoubleValue(300), VBByteType.TypeInfo, ThrowawayExpression);

        Assert.AreEqual((int)VBRuntimeErrorId.Overflow, analysis.Builder.Build().Errors.Single().ErrorId);
    }
}
