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
using RDCore.SDK.Semantics.Facts;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// A golden master of the arithmetic operators: every operator over every pair of a broad set of operand values - what each operation yields or raises, and the
/// facts an analysis is told of it - compared line for line with what was recorded before the operation plans were extracted from the operator semantics (IR
/// lowering S0.2). A line that differs is a change in behaviour.
/// </summary>
/// <remarks>
/// The snapshot is <c>GoldenMasters/arithmetic-operators.txt.gz</c>, next to this file: a line to an operation, compressed (it is some 11,000 lines). A change
/// that is meant is recorded by running this test with <c>RDCORE_UPDATE_GOLDEN_MASTERS=1</c>; the test prints the lines that differ when it fails, which is
/// how a change to the snapshot is reviewed.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.6.9.3 Arithmetic Operators")]
public sealed class ArithmeticOperatorsGoldenMasterTests : LetCoercionRuntimeSemanticsTests
{
    private const string UpdateVariable = "RDCORE_UPDATE_GOLDEN_MASTERS";

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
    public void TheArithmeticOperators_DoWhatTheyWereRecordedToDo()
    {
        var actual = Record();
        var path = SnapshotPath();

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var file = File.Create(path))
            using (var compressed = new GZipStream(file, CompressionLevel.SmallestSize))
            {
                compressed.Write(Encoding.UTF8.GetBytes(actual));
            }

            Assert.Inconclusive($"The golden master was recorded: {path}");
        }

        Assert.IsTrue(File.Exists(path), $"No golden master at {path}: run with {UpdateVariable}=1 to record one.");
        string[] expected;
        using (var file = File.OpenRead(path))
        using (var compressed = new GZipStream(file, CompressionMode.Decompress))
        using (var reader = new StreamReader(compressed, Encoding.UTF8))
        {
            expected = reader.ReadToEnd().Split('\n');
        }

        var lines = actual.Split('\n');

        var differences = expected.Zip(lines).Where(pair => pair.First != pair.Second).Take(20)
            .Select(pair => $"- {pair.First}{Environment.NewLine}+ {pair.Second}").ToArray();
        Assert.IsEmpty(differences, $"The operators no longer do what they were recorded to do:{Environment.NewLine}{string.Join(Environment.NewLine, differences)}");
        Assert.HasCount(expected.Length, lines, "as many operations as were recorded");
    }

    private static string SnapshotPath([CallerFilePath] string source = "")
        => Path.Combine(Path.GetDirectoryName(source)!, "GoldenMasters", "arithmetic-operators.txt.gz");

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
                    text.Append($"{Describe(left)} {token} {Describe(right)} => {Describe(result)}{Describe(observer)}\n");
                }
            }
        }

        foreach (var operand in Values())
        {
            var (observer, operators) = Observed();
            var result = operators.EvaluateUnaryOperator(Session,
                new VBUnaryOperatorExpressionNode(Tokens.NegationOp, NodeId, TestLocations.TestLocation, [Operand()]), operand);
            text.Append($"{Tokens.NegationOp} {Describe(operand)} => {Describe(result)}{Describe(observer)}\n");
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

    private static string Describe(VBTypedValue value) => value switch
    {
        { IsIndeterminate: true } => $"{value.TypeInfo.Name}(?)",
        VBVariantValue variant => $"Variant({Describe(variant.TypedValue)})",
        VBNullValue or VBEmptyValue => value.TypeInfo.Name,
        VBObjectValue => "Nothing",
        _ => $"{value.TypeInfo.Name}({Format(value.RuntimeValue.BoxedValue)})",
    };

    private static string Format(object? value) => value switch
    {
        double number => number.ToString("R", CultureInfo.InvariantCulture),
        float number => number.ToString("R", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => $"{value}",
    };

    private static string Describe(SDK.Runtime.Shared.RuntimeSemanticsEvaluationResult result)
        => result.ErrorInfo is { } error ? $"error {error.ErrorId}" : result.Result is { } value ? Describe(value) : "nothing";

    private static string Describe(RecordingAnalysisObserver observer)
    {
        var facts = observer.Operations.Select(fact => fact switch
            {
                ArithmeticOperatorFact arithmetic => $"op {arithmetic.EffectiveType?.Name ?? "-"} [{arithmetic.Flags}] known={arithmetic.IsValueKnown} error={arithmetic.Error?.ErrorId}",
                _ => $"op {fact.GetType().Name} {fact.EffectiveType?.Name ?? "-"} known={fact.IsValueKnown} error={fact.Error?.ErrorId}",
            })
            .Concat(observer.Conversions.Select(fact =>
                $"conv #{fact.Operand} {fact.Source.Name}->{fact.Destination.Name} [{fact.Flags}] known={fact.IsValueKnown} error={fact.Error?.ErrorId}"));
        return string.Concat(facts.Select(fact => $" | {fact}"));
    }
}
