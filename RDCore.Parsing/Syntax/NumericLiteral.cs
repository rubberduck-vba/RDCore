using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using System.Globalization;

namespace RDCore.Parsing.Syntax;

/// <summary>
/// Resolves a numeric literal token to its statically-typed <see cref="VBTypedValue"/> per
/// <c>[MS-VBAL] §3.3.2</c> / <c>RD-VBAL §3.2.0.1</c>. Shared by the declaration pass and the
/// conditional-compilation pass so the two cannot drift.
/// <list type="bullet">
/// <item>A type-declaration character (<c>% &amp; ^ ! # @</c>) forces the type; a value that does not
/// fit is an overflow.</item>
/// <item>An unsuffixed decimal integer takes the smallest of <c>Integer</c>, <c>Long</c>, <c>Double</c>
/// that holds it (never <c>LongLong</c>).</item>
/// <item>An unsuffixed decimal floating-point literal (fraction or exponent — the exponent letter is
/// <c>[DEde]</c>) is <c>Double</c>.</item>
/// <item>An <c>&amp;H…</c> / <c>&amp;O…</c> literal is typed by <em>bit width</em>, two's-complement:
/// narrowest of <c>Integer</c> (16-bit) / <c>Long</c> (32-bit) unsuffixed; <c>%</c>/<c>&amp;</c>/<c>^</c>
/// set the width. Unsuffixed beyond 32 bits is an overflow (MS-VBA's cryptic "expected: expression").
/// A radix literal is never <c>Double</c>.</item>
/// </list>
/// </summary>
internal static class NumericLiteral
{
    private const string TypeHintChars = "%&^!#@";

    /// <summary>
    /// The resolved value, and <c>true</c> when the literal is out of range for its type — the caller
    /// attaches a located <c>NumericLiteralOverflow</c> syntax error. An out-of-range literal still
    /// yields a node, with <see cref="VBUnknownValue.DefaultValue"/>.
    /// </summary>
    public static (VBTypedValue Value, bool Overflow) Resolve(string token)
    {
        var (digits, hint) = SplitTypeHint(token);

        if (digits.StartsWith("&H", StringComparison.OrdinalIgnoreCase))
        {
            return Radix(digits[2..], fromBase: 16, hint);
        }
        if (digits.StartsWith("&O", StringComparison.OrdinalIgnoreCase))
        {
            return Radix(digits[2..], fromBase: 8, hint);
        }
        // a `! # @` suffix forces the float/currency family even on an integer digit string (`1#`).
        return hint is '!' or '#' or '@' || IsFloat(digits) ? Real(digits, hint) : Integer(digits, hint);
    }

    private static (VBTypedValue, bool) Unresolved => (VBUnknownValue.DefaultValue, true);

    private static (string digits, char hint) SplitTypeHint(string text)
        => text.Length > 0 && TypeHintChars.IndexOf(text[^1]) >= 0 ? (text[..^1], text[^1]) : (text, '\0');

    // a decimal literal is floating-point when it has a fraction or an exponent ([DEde]).
    private static bool IsFloat(string digits)
        => digits.Contains('.') || digits.IndexOfAny(['e', 'E', 'd', 'D']) >= 0;

    private static (VBTypedValue, bool) Real(string digits, char hint)
    {
        // the exponent letter is [DEde]; double.Parse only understands E/e.
        var normalized = digits.Replace('D', 'E').Replace('d', 'e');
        if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return Unresolved;
        }

        return hint switch
        {
            '!' => float.IsFinite((float)value) ? (new VBSingleValue((float)value), false) : Unresolved,
            '@' => decimal.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var money)
                ? (new VBCurrencyValue(money), false)
                : Unresolved,
            '#' or '\0' => double.IsFinite(value) ? (new VBDoubleValue(value), false) : Unresolved,
            // %, &, ^ on a value with a fraction/exponent: force the integral type, truncating.
            _ => double.IsFinite(value) && value is >= 0 and <= long.MaxValue ? Integral(hint, (ulong)value) : Unresolved,
        };
    }

    private static (VBTypedValue, bool) Integer(string digits, char hint)
    {
        // literals are unsigned — a leading '-' is a unary operator, not part of the token.
        if (ulong.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var magnitude))
        {
            return hint == '\0' ? SmallestFit(magnitude) : Integral(hint, magnitude);
        }
        // too many digits for UInt64 — fall to a Double approximation for the unsuffixed / '#' case.
        if (hint is '\0' or '#'
            && double.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var approx)
            && double.IsFinite(approx))
        {
            return (new VBDoubleValue(approx), false);
        }
        return Unresolved;
    }

    // MS-VBAL §3.3.2: smallest of Integer / Long / Double; an unsuffixed integer never widens to LongLong.
    private static (VBTypedValue, bool) SmallestFit(ulong magnitude) => magnitude switch
    {
        <= (ulong)short.MaxValue => (new VBIntegerValue((short)magnitude), false),
        <= int.MaxValue => (new VBLongValue((int)magnitude), false),
        _ => (new VBDoubleValue(magnitude), false),
    };

    private static (VBTypedValue, bool) Integral(char hint, ulong magnitude) => hint switch
    {
        '%' => magnitude <= (ulong)short.MaxValue ? (new VBIntegerValue((short)magnitude), false) : Unresolved,
        '&' => magnitude <= int.MaxValue ? (new VBLongValue((int)magnitude), false) : Unresolved,
        '^' => magnitude <= long.MaxValue ? (new VBLongLongValue((long)magnitude), false) : Unresolved,
        '@' => magnitude <= 922_337_203_685_477_580UL ? (new VBCurrencyValue(magnitude), false) : Unresolved,
        _ => SmallestFit(magnitude),
    };

    private static (VBTypedValue, bool) Radix(string digits, int fromBase, char hint)
    {
        ulong bits;
        try
        {
            bits = Convert.ToUInt64(digits, fromBase);
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException)
        {
            return Unresolved;
        }

        // typed by bit width, two's-complement at that width; a radix literal is never Double.
        return hint switch
        {
            '%' => bits <= 0xFFFF ? (new VBIntegerValue(unchecked((short)(ushort)bits)), false) : Unresolved,
            '&' => bits <= 0xFFFFFFFF ? (new VBLongValue(unchecked((int)(uint)bits)), false) : Unresolved,
            '^' => (new VBLongLongValue(unchecked((long)bits)), false),
            _ when bits <= 0xFFFF => (new VBIntegerValue(unchecked((short)(ushort)bits)), false),
            _ when bits <= 0xFFFFFFFF => (new VBLongValue(unchecked((int)(uint)bits)), false),
            _ => Unresolved,
        };
    }
}
