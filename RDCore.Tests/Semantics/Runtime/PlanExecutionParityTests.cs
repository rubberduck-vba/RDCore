using RDCore.Runtime.Execution;
using RDCore.Runtime.Semantics;
using RDCore.Runtime.Semantics.Operators;
using RDCore.Runtime.Semantics.Plans;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Expressions;
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
using RDCore.SDK.Semantics.Analysis;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// An operation evaluated by its plan (IR lowering §3.2) is the operation the evaluation pipeline evaluates: the same value of the same type, or the same
/// run-time error - for every operator, over operands chosen to overflow, divide by zero, round, and be held in a Variant.
/// </summary>
/// <remarks>
/// An observed operation always goes through the pipeline, and one nothing observes is executed by its plan when it can be: the two providers below evaluate
/// each operation both ways.
/// </remarks>
[TestClass]
[TestCategory("RD-VBAL §5.0.2.1 Operator Evaluation")]
public sealed class PlanExecutionParityTests : LetCoercionRuntimeSemanticsTests
{
    private sealed class NoSymbols : ISymbolProvider
    {
        public IEnumerable<Symbol> ProvideSymbols() => [];
    }

    private static readonly IRuntimeSession Session = RuntimeSessionComposer.Compose(
        new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, SupportsOptionCompareDatabase: true, OptionCompare.Text), new NoSymbols());

    private static LiteralExpressionNode Operand() => new(NodeId, TestLocations.TestLocation, new VBLongValue(1));

    // a provider owns its coercion stack: one per operation, as tests run in parallel.
    private static IOperatorRuntimeSemanticsProvider Planned() => new OperatorRuntimeSemanticsProvider(LetCoercionAnalysisHarness.BuildProvider(), Formatter());

    private static IOperatorRuntimeSemanticsProvider Pipelined()
    {
        var observation = new AnalysisObservation(new RecordingAnalysisObserver());
        return new OperatorRuntimeSemanticsProvider(LetCoercionAnalysisHarness.BuildProvider(observation), Formatter(), observation);
    }

    private static readonly string[] BinaryTokens =
    [
        Tokens.AdditionOp, Tokens.SubtractionOp, Tokens.MultiplicationOp, Tokens.DivisionOp, Tokens.IntegerDivisionOp, Tokens.ModuloOp,
        Tokens.PowerOp, Tokens.ConcatOp, Tokens.CompareIsOp, Tokens.CompareEqualOp, Tokens.CompareNotEqualOp, Tokens.CompareGreaterThanOp,
        Tokens.CompareGreaterThanOrEqualOp, Tokens.CompareLessThanOp, Tokens.CompareLessThanOrEqualOp, Tokens.CompareLikeOp,
        Tokens.LogicalAndOp, Tokens.LogicalOrOp, Tokens.LogicalXOrOp, Tokens.LogicalEqvOp, Tokens.LogicalImpOp,
    ];

    private static readonly string[] UnaryTokens = [Tokens.NegationOp, Tokens.LogicalNotOp];

    // the values a plan is executed for - zero, a fraction to round, each type's extremes, which overflow - held as they are and in a Variant, and a few the
    // plan declines, so that declining is seen to change nothing either.
    private static VBTypedValue[] Values() =>
    [
        new VBByteValue(0), new VBByteValue(255),
        new VBBooleanValue(false), new VBBooleanValue(true),
        new VBIntegerValue(0), new VBIntegerValue(-7), new VBIntegerValue(short.MaxValue), new VBIntegerValue(short.MinValue),
        new VBLongValue(0), new VBLongValue(7), new VBLongValue(int.MaxValue), new VBLongValue(int.MinValue),
        // a LongLong past the range of a Long; its extremes are not converted without an exception yet (KnownGapsFoundByPlanExecutionTests).
        new VBLongLongValue(0), new VBLongLongValue(5_000_000_000),
        new VBSingleValue(0), new VBSingleValue(2.5f), new VBSingleValue(3e38f),
        new VBDoubleValue(0), new VBDoubleValue(3.5), new VBDoubleValue(-2.5), new VBDoubleValue(1e300),
        new VBCurrencyValue(0), new VBCurrencyValue(2.5m),
        new VBDecimalValue(0), new VBDecimalValue(2.5m),
        new VBDateValue(0), new VBDateValue(2.5),
        new VBStringValue("7"),
        VBEmptyValue.Empty,
        VBNullValue.Null,
        new VBVariantValue(new VBIntegerValue(short.MaxValue)), new VBVariantValue(new VBLongValue(int.MaxValue)), new VBVariantValue(new VBDoubleValue(2.5)),
        new VBVariantValue(new VBByteValue(255)), new VBVariantValue(new VBBooleanValue(true)), new VBVariantValue(VBEmptyValue.Empty),
    ];

    private static readonly VBType[] DeclaredTypes =
    [
        VBByteType.TypeInfo, VBBooleanType.TypeInfo, VBIntegerType.TypeInfo, VBLongType.TypeInfo, VBLongLongType.TypeInfo, VBSingleType.TypeInfo,
        VBDoubleType.TypeInfo, VBCurrencyType.TypeInfo, VBDecimalType.TypeInfo, VBDateType.TypeInfo, VBStringType.TypeInfo, VBVariantType.TypeInfo,
    ];

    [TestMethod]
    public void EveryBinaryOperation_ExecutedByItsPlan_IsTheOperationThePipelineEvaluates()
    {
        var mismatches = new List<string>();
        foreach (var token in BinaryTokens)
        {
            foreach (var left in Values())
            {
                foreach (var right in Values())
                {
                    var expression = new VBBinaryOperatorExpressionNode(token, NodeId, TestLocations.TestLocation, Operand(), Operand());
                    Compare(mismatches, $"{Describe(left)} {token} {Describe(right)}",
                        Planned().EvaluateBinaryOperator(Session, expression, left, right),
                        Pipelined().EvaluateBinaryOperator(Session, expression, left, right));
                }
            }
        }

        Assert.IsEmpty(mismatches, Report(mismatches));
    }

    [TestMethod]
    public void EveryUnaryOperation_ExecutedByItsPlan_IsTheOperationThePipelineEvaluates()
    {
        var mismatches = new List<string>();
        foreach (var token in UnaryTokens)
        {
            foreach (var operand in Values())
            {
                var expression = new VBUnaryOperatorExpressionNode(token, NodeId, TestLocations.TestLocation, [Operand()]);
                Compare(mismatches, $"{token} {Describe(operand)}",
                    Planned().EvaluateUnaryOperator(Session, expression, operand),
                    Pipelined().EvaluateUnaryOperator(Session, expression, operand));
            }
        }

        Assert.IsEmpty(mismatches, Report(mismatches));
    }

    // a let-coercion the plan executes has the value the coercion has when its strategy evaluates it; one that fails is never executed by the plan, so that
    // the strategy raises the error.
    [TestMethod]
    public void EveryLetCoercion_ExecutedByItsPlan_IsTheCoercionItsStrategyEvaluates()
    {
        var mismatches = new List<string>();
        foreach (var destination in DeclaredTypes)
        {
            foreach (var source in Values())
            {
                if (!PlanExecutor.TryConvert(source, destination, out var planned))
                {
                    continue;
                }

                var evaluated = LetCoercionAnalysisHarness.BuildProvider().EvaluateLetCoercionSemantics(Session.Symbols.Resolver, ThrowawayExpression,
                    new LetCoercionStackFrame(NodeId, InputIndex.CoercionSourceValue, source, new VBTypeDescValue(destination)));
                Compare(mismatches, $"{Describe(source)} -> {destination.Name}",
                    RuntimeSemanticsEvaluationResult.Success(planned),
                    evaluated.IsSuccess ? RuntimeSemanticsEvaluationResult.Success(evaluated.Result!) : RuntimeSemanticsEvaluationResult.Error(evaluated.ErrorInfo!));
            }
        }

        Assert.IsEmpty(mismatches, Report(mismatches));
    }

    private static void Compare(List<string> mismatches, string operation, RuntimeSemanticsEvaluationResult planned, RuntimeSemanticsEvaluationResult pipelined)
    {
        var (plannedOutcome, pipelinedOutcome) = (Outcome(planned), Outcome(pipelined));
        if (plannedOutcome != pipelinedOutcome)
        {
            mismatches.Add($"{operation}: planned {plannedOutcome}, pipelined {pipelinedOutcome}");
        }
    }

    private static string Outcome(RuntimeSemanticsEvaluationResult result)
        => result.IsSuccess ? Describe(result.Result!) : $"error {result.ErrorInfo?.ErrorId}";

    private static string Report(List<string> mismatches)
        => $"{mismatches.Count} mismatch(es):{Environment.NewLine}{string.Join(Environment.NewLine, mismatches)}";

    private static string Describe(VBTypedValue value) => value switch
    {
        VBVariantValue variant => $"Variant({Describe(variant.TypedValue)})",
        VBNullValue or VBEmptyValue => value.TypeInfo.Name,
        VBObjectValue => "Nothing",
        _ => $"{value.TypeInfo.Name}({value.RuntimeValue.BoxedValue})",
    };
}
