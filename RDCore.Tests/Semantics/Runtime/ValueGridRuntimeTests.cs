using RDCore.Runtime.Execution;
using RDCore.Runtime.Semantics.Operators;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.Operators;
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

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// Every operator and every let-coercion, over every value type the language has: whatever an operation yields or raises, it is what the specification
/// says - never an internal error, and never an exception.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.0.2.1 Operator Evaluation")]
public sealed class ValueGridRuntimeTests : LetCoercionRuntimeSemanticsTests
{
    private sealed class NoSymbols : ISymbolProvider
    {
        public IEnumerable<Symbol> ProvideSymbols() => [];
    }

    private static readonly IRuntimeSession Session = RuntimeSessionComposer.Compose(
        new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, SupportsOptionCompareDatabase: true, OptionCompare.Text), new NoSymbols());

    private static LiteralExpressionNode Operand() => new(NodeId, TestLocations.TestLocation, new VBLongValue(1));

    // a provider owns its coercion stack: one per operation, as tests run in parallel.
    private static IOperatorRuntimeSemanticsProvider Operators() => new OperatorRuntimeSemanticsProvider(LetCoercionAnalysisHarness.BuildProvider(), Formatter());

    private static readonly string[] BinaryTokens =
    [
        Tokens.AdditionOp, Tokens.SubtractionOp, Tokens.MultiplicationOp, Tokens.DivisionOp, Tokens.IntegerDivisionOp, Tokens.ModuloOp,
        Tokens.PowerOp, Tokens.ConcatOp, Tokens.CompareIsOp, Tokens.CompareEqualOp, Tokens.CompareNotEqualOp, Tokens.CompareGreaterThanOp,
        Tokens.CompareGreaterThanOrEqualOp, Tokens.CompareLessThanOp, Tokens.CompareLessThanOrEqualOp, Tokens.CompareLikeOp,
        Tokens.LogicalAndOp, Tokens.LogicalOrOp, Tokens.LogicalXOrOp, Tokens.LogicalEqvOp, Tokens.LogicalImpOp,
    ];

    private static readonly string[] UnaryTokens = [Tokens.NegationOp, Tokens.LogicalNotOp, OperatorSymbolNames.UnaryLetCoerceOp];

    // each value type's default value and another of its values.
    private static VBTypedValue[] Values() =>
    [
        new VBByteValue(0), new VBByteValue(7),
        new VBBooleanValue(false), new VBBooleanValue(true),
        new VBIntegerValue(0), new VBIntegerValue(7),
        new VBLongValue(0), new VBLongValue(7),
        new VBLongLongValue(0), new VBLongLongValue(7),
        new VBSingleValue(0), new VBSingleValue(2.5f),
        new VBDoubleValue(0), new VBDoubleValue(2.5),
        new VBCurrencyValue(0), new VBCurrencyValue(2.5m),
        new VBDecimalValue(0), new VBDecimalValue(2.5m),
        new VBDateValue(0), new VBDateValue(2),
        new VBStringValue(string.Empty), new VBStringValue("7"),
        VBEmptyValue.Empty,
        VBNullValue.Null,
        new VBErrorValue(5),
        VBObjectValue.Nothing,
        new VBVariantValue(new VBLongValue(7)), new VBVariantValue(new VBStringValue("7")),
    ];

    private static readonly VBType[] DeclaredTypes =
    [
        VBByteType.TypeInfo, VBBooleanType.TypeInfo, VBIntegerType.TypeInfo, VBLongType.TypeInfo, VBLongLongType.TypeInfo, VBSingleType.TypeInfo,
        VBDoubleType.TypeInfo, VBCurrencyType.TypeInfo, VBDecimalType.TypeInfo, VBDateType.TypeInfo, VBStringType.TypeInfo, new VBFixedStringType(3),
        VBVariantType.TypeInfo, VBObjectType.TypeInfo, VBErrorType.TypeInfo, VBResizableByteArrayType.TypeInfo,
    ];

    [TestMethod]
    public void EveryBinaryOperator_OfEveryPairOfValueTypes_RaisesNoInternalError()
    {
        var failures = new List<string>();
        foreach (var token in BinaryTokens)
        {
            foreach (var left in Values())
            {
                foreach (var right in Values())
                {
                    Probe(failures, $"{Describe(left)} {token} {Describe(right)}", () => Operators().EvaluateBinaryOperator(Session,
                        new VBBinaryOperatorExpressionNode(token, NodeId, TestLocations.TestLocation, Operand(), Operand()), left, right).ErrorInfo?.ErrorId);
                }
            }
        }

        Assert.IsEmpty(failures, Report(failures));
    }

    [TestMethod]
    public void EveryUnaryOperator_OfEveryValueType_RaisesNoInternalError()
    {
        var failures = new List<string>();
        foreach (var token in UnaryTokens)
        {
            foreach (var operand in Values())
            {
                Probe(failures, $"{token} {Describe(operand)}", () => Operators().EvaluateUnaryOperator(Session,
                    new VBUnaryOperatorExpressionNode(token, NodeId, TestLocations.TestLocation, [Operand()]), operand).ErrorInfo?.ErrorId);
            }
        }

        Assert.IsEmpty(failures, Report(failures));
    }

    [TestMethod]
    public void TheLetCoercion_OfEveryValueType_ToEveryDeclaredType_RaisesNoInternalError()
    {
        var failures = new List<string>();
        foreach (var destination in DeclaredTypes)
        {
            foreach (var source in Values())
            {
                Probe(failures, $"{Describe(source)} -> {destination.Name}", () => LetCoercionAnalysisHarness.BuildProvider()
                    .EvaluateLetCoercionSemantics(Session.Symbols.Resolver, ThrowawayExpression,
                        new LetCoercionStackFrame(NodeId, InputIndex.CoercionSourceValue, source, new VBTypeDescValue(destination))).ErrorInfo?.ErrorId);
            }
        }

        Assert.IsEmpty(failures, Report(failures));
    }

    private static void Probe(List<string> failures, string operation, Func<int?> errorIdOf)
    {
        try
        {
            if (errorIdOf() == (int)VBRuntimeErrorId.InternalError)
            {
                failures.Add($"{operation}: internal error");
            }
        }
        catch (Exception exception)
        {
            failures.Add($"{operation}: {exception.GetType().Name} {exception.Message}");
        }
    }

    private static string Report(List<string> failures)
        => $"{failures.Count} failure(s):{Environment.NewLine}{string.Join(Environment.NewLine, failures)}";

    private static string Describe(VBTypedValue value) => value switch
    {
        VBVariantValue variant => $"Variant({Describe(variant.TypedValue)})",
        VBNullValue or VBEmptyValue => value.TypeInfo.Name,
        VBObjectValue => "Nothing",
        _ => $"{value.TypeInfo.Name}({value.RuntimeValue.BoxedValue})",
    };
}
