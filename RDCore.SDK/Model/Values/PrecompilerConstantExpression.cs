using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace RDCore.SDK.Model.Values;

/// <summary>
/// Parses the literal source text of a project-level <c>#Const</c> value (from the <c>.rdproj</c> or a
/// <c>--define</c> argument) into a <see cref="VBTypedValue"/>, following the literal-typing rules of
/// <strong>MS-VBAL §3.3.2</strong>.
/// </summary>
/// <remarks>
/// 👉 Only bare literals (optionally with a leading <c>-</c>) are recognised — the full
/// <c>&lt;cc-expression&gt;</c> operator grammar is a semantic-layer concern. An unrecognised value is
/// rejected rather than guessed.
/// </remarks>
public static class PrecompilerConstantExpression
{
    public static bool TryParse(string? source, [NotNullWhen(true)] out VBTypedValue? value)
    {
        value = null;
        var text = source?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        if (text.Equals("True", StringComparison.OrdinalIgnoreCase)) { value = new VBBooleanValue(true); return true; }
        if (text.Equals("False", StringComparison.OrdinalIgnoreCase)) { value = new VBBooleanValue(false); return true; }
        if (text.Equals("Empty", StringComparison.OrdinalIgnoreCase)) { value = VBEmptyValue.Empty; return true; }
        if (text.Equals("Null", StringComparison.OrdinalIgnoreCase)) { value = VBNullValue.Null; return true; }
        if (text.Equals("Nothing", StringComparison.OrdinalIgnoreCase)) { value = VBObjectValue.Nothing; return true; }

        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
        {
            value = new VBStringValue(text[1..^1].Replace("\"\"", "\""));
            return true;
        }

        if (text.Length >= 2 && text[0] == '#' && text[^1] == '#'
            && DateTime.TryParse(text[1..^1], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            value = new VBDateValue(date.ToOADate());
            return true;
        }

        var numeric = text;
        var negative = numeric.StartsWith('-');
        if (negative || numeric.StartsWith('+'))
        {
            numeric = numeric[1..].TrimStart();
        }

        if (!numeric.Contains('.') && !numeric.Contains('E', StringComparison.OrdinalIgnoreCase)
            && long.TryParse(numeric, NumberStyles.None, CultureInfo.InvariantCulture, out var integral))
        {
            if (negative) { integral = -integral; }
            // MS-VBAL 3.3.2: an unsuffixed integer literal is the smallest of Integer, Long, Double.
            value = integral switch
            {
                >= short.MinValue and <= short.MaxValue => new VBIntegerValue((short)integral),
                >= int.MinValue and <= int.MaxValue => new VBLongValue((int)integral),
                _ => new VBDoubleValue(integral),
            };
            return true;
        }

        if (double.TryParse(numeric, NumberStyles.Float, CultureInfo.InvariantCulture, out var real))
        {
            value = new VBDoubleValue(negative ? -real : real);
            return true;
        }

        return false;
    }
}
