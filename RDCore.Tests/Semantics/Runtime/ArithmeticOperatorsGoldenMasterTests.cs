using RDCore.Runtime.Execution;
using RDCore.Runtime.Semantics;
using RDCore.Runtime.Semantics.Operators;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using System.Text;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// A golden master of the arithmetic operators: every operator over every pair of a broad set of operand values - what each operation yields or raises, and the
/// facts an analysis is told of it - compared line for line with what was recorded before the operation plans were extracted from the operator semantics. A line that differs is a change in behaviour.
/// </summary>
/// <remarks>
/// The snapshot is <c>GoldenMasters/arithmetic-operators.txt.gz</c>, next to this file: some 11,000 lines, recorded and compared as <see cref="GoldenMaster"/>
/// says.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.6.9.3 Arithmetic Operators")]
public sealed class ArithmeticOperatorsGoldenMasterTests : LetCoercionRuntimeSemanticsTests
{
    private sealed class NoSymbols : ISymbolProvider
    {
        public IEnumerable<Symbol> ProvideSymbols() => [];
    }

    private static readonly IRuntimeSession Session = RuntimeSessionComposer.Compose(
        new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, SupportsOptionCompareDatabase: true, OptionCompare.Text), new NoSymbols());

    private static readonly string[] Tokens_ =
    [
        Tokens.AdditionOp, Tokens.SubtractionOp, Tokens.MultiplicationOp, Tokens.DivisionOp, Tokens.IntegerDivisionOp, Tokens.ModuloOp, Tokens.PowerOp,
    ];

    // each value type's default and another of its values, the values at the edges of the integral ranges, and the operands only a Variant or an analysis has.
    private static VBTypedValue[] Values() =>
    [
        new VBByteValue(0), new VBByteValue(7), new VBByteValue(255),
        new VBBooleanValue(false), new VBBooleanValue(true),
        new VBIntegerValue(0), new VBIntegerValue(7), new VBIntegerValue(-7), new VBIntegerValue(32767),
        new VBLongValue(0), new VBLongValue(7), new VBLongValue(2147483647),
        new VBLongLongValue(0), new VBLongLongValue(7),
        new VBSingleValue(0), new VBSingleValue(2.5f),
        new VBDoubleValue(0), new VBDoubleValue(2.5), new VBDoubleValue(-2.5),
        new VBCurrencyValue(0), new VBCurrencyValue(2.5m),
        new VBDecimalValue(0), new VBDecimalValue(2.5m),
        new VBDateValue(0), new VBDateValue(2),
        new VBStringValue(string.Empty), new VBStringValue("7"), new VBStringValue("2.5"), new VBStringValue("abc"),
        VBEmptyValue.Empty,
        VBNullValue.Null,
        new VBErrorValue(5),
        VBObjectValue.Nothing,
        new VBVariantValue(new VBLongValue(7)), new VBVariantValue(new VBStringValue("7")), new VBVariantValue(VBEmptyValue.Empty), new VBVariantValue(VBNullValue.Null),
        VBLongType.TypeInfo.CreateIndeterminateValue(), VBDoubleType.TypeInfo.CreateIndeterminateValue(), VBVariantType.TypeInfo.CreateIndeterminateValue(),
    ];

    private static LiteralExpressionNode Operand() => new(NodeId, TestLocations.TestLocation, new VBLongValue(1));

    [TestMethod]
    public void TheArithmeticOperators_DoWhatTheyWereRecordedToDo() => GoldenMaster.AssertMatches(Record(), "arithmetic-operators.txt.gz");

    private static string Record()
    {
        var text = new StringBuilder();
        foreach (var token in Tokens_)
        {
            foreach (var left in Values())
            {
                foreach (var right in Values())
                {
                    var (observer, operators) = Observed();
                    var result = operators.EvaluateBinaryOperator(Session,
                        new VBBinaryOperatorExpressionNode(token, NodeId, TestLocations.TestLocation, Operand(), Operand()), left, right);
                    text.Append($"{GoldenMaster.Describe(left)} {token} {GoldenMaster.Describe(right)} => {GoldenMaster.Describe(result)}{GoldenMaster.Describe(observer)}\n");
                }
            }
        }

        foreach (var operand in Values())
        {
            var (observer, operators) = Observed();
            var result = operators.EvaluateUnaryOperator(Session,
                new VBUnaryOperatorExpressionNode(Tokens.NegationOp, NodeId, TestLocations.TestLocation, [Operand()]), operand);
            text.Append($"{Tokens.NegationOp} {GoldenMaster.Describe(operand)} => {GoldenMaster.Describe(result)}{GoldenMaster.Describe(observer)}\n");
        }

        return text.ToString().TrimEnd('\n');
    }

    // a provider owns its coercion stack: one per operation. Observed, so that what an analysis is told of the operation is recorded with it.
    private static (RecordingAnalysisObserver Observer, IOperatorRuntimeSemanticsProvider Operators) Observed()
    {
        var observer = new RecordingAnalysisObserver();
        var observation = new AnalysisObservation(observer);
        return (observer, new OperatorRuntimeSemanticsProvider(LetCoercionAnalysisHarness.BuildProvider(observation), Formatter(), observation));
    }
}
