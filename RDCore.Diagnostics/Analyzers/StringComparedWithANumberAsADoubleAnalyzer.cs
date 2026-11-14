using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Semantics.Facts;
using System.Numerics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC11027</c>: a relational operator compares a <c>String</c> with a <c>Byte</c>, an <c>Integer</c> or a <c>Long</c> - or a <c>Variant</c> holding a
/// <c>String</c> with a <c>Single</c> or a <c>LongLong</c> - and MS-VBA compares the two as <c>Double</c> values, where <strong>MS-VBAL §5.6.9.5</strong>
/// compares them in the number's type.
/// </summary>
/// <remarks>
/// The analyzer reads the effective type of the comparison, and what the language core states of its <c>String</c> operand
/// (<see cref="StringOperandFact"/>): its value Let-coerced to a <c>Double</c> and to the specified effective type, and the error each raises. It reports a
/// comparison whose result can differ from the specification's. RD-VBA compares as MS-VBA does: <c>"2.5" = 2</c> is <c>False</c>, where the specification
/// Let-coerces <c>"2.5"</c> to <c>2</c>; and <c>"40000"</c> compared with an <c>Integer</c> does not overflow. A warning, equivalent to the departure. Nothing
/// is reported for a procedure that was not evaluated.
/// </remarks>
internal sealed class StringComparedWithANumberAsADoubleAnalyzer : IModuleAnalyzer
{
    private const string Double = "Double";

    public AnalysisPhase Phase => AnalysisPhase.Runtime;

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => (context.Semantics?.Procedures ?? [])
            .SelectMany(procedure => procedure.Runtime?.Operations ?? [])
            .Where(op => op is { EffectiveType: Double, StringOperand: { SpecifiedEffectiveType: not Double } operand } && CanAnswerOtherwise(op.Operator, operand))
            .Select(op => new AnalyzerFinding(
                RDCoreDiagnosticId.StringComparedWithANumberAsADouble.ToCode(),
                (op.OperatorLocation ?? op.Location).Range,
                DiagnosticSeverity.Warning,
                AnalyzerMessages.Format(
                    RDCoreDiagnosticsResources.StringComparedWithANumberAsADouble_Message,
                    op.Operator)));

    // whether the comparison of the String's Double with the number can answer otherwise than the comparison of the String's value in the specified type. It
    // cannot for a String that both Let-coercions refuse alike ("abc" is 13 either way), nor for a known String and a known number the two answer alike for.
    // With the number not known, the two answer alike for every number but those next to the String's value: "2.5" is the table's 2, and 2.5 < n is 2 < n for
    // every integer n, where 2.5 > n is not 2 > n for n = 2. Past 2^53, where a Double does not hold every integer, a LongLong that is not known is not
    // compared alike.
    private static bool CanAnswerOtherwise(string op, StringOperandFact operand)
    {
        if (operand.AsSpecifiedError is { } error)
        {
            return operand.AsDoubleError != error;
        }

        if (operand.AsSpecified is not { } tabled || operand.AsDouble is not { } asDouble)
        {
            return true;
        }

        var held = Convert.ToDouble(asDouble.RuntimeValue.BoxedValue);
        if (operand.SpecifiedEffectiveType == "Single")
        {
            var single = Convert.ToSingle(tabled.RuntimeValue.BoxedValue);
            float[] singles = operand.Number is { } number ? [Convert.ToSingle(number.RuntimeValue.BoxedValue)] : [float.BitDecrement(single), single, float.BitIncrement(single)];
            return singles.Any(n => Compare(op, held, (double)n, operand.IsLeft) != Compare(op, single, n, operand.IsLeft));
        }

        var integer = Convert.ToInt64(tabled.RuntimeValue.BoxedValue);
        if (operand.Number is { } known)
        {
            var value = Convert.ToInt64(known.RuntimeValue.BoxedValue);
            return Compare(op, held, (double)value, operand.IsLeft) != Compare(op, integer, value, operand.IsLeft);
        }

        if (Math.Abs(held) >= 1L << 53)
        {
            return true;
        }

        long[] integers = [(long)Math.Floor(held) - 1, (long)Math.Floor(held), (long)Math.Ceiling(held), (long)Math.Ceiling(held) + 1];
        return integers.Where(n => IsInRangeOf(operand.SpecifiedEffectiveType, n))
            .Any(n => Compare(op, held, (double)n, operand.IsLeft) != Compare(op, integer, n, operand.IsLeft));
    }

    // the answer of the operator, with the String's value on the side it is written.
    private static bool Compare<T>(string op, T text, T number, bool textIsLeft) where T : INumber<T>
    {
        var (left, right) = textIsLeft ? (text, number) : (number, text);
        return op switch
        {
            Tokens.CompareEqualOp => left == right,
            Tokens.CompareNotEqualOp => left != right,
            Tokens.CompareLessThanOp => left < right,
            Tokens.CompareLessThanOrEqualOp => left <= right,
            Tokens.CompareGreaterThanOp => left > right,
            Tokens.CompareGreaterThanOrEqualOp => left >= right,
            _ => false,
        };
    }

    private static bool IsInRangeOf(string type, long value) => type switch
    {
        "Byte" => value is >= byte.MinValue and <= byte.MaxValue,
        "Integer" => value is >= short.MinValue and <= short.MaxValue,
        "Long" => value is >= int.MinValue and <= int.MaxValue,
        _ => true,
    };
}
