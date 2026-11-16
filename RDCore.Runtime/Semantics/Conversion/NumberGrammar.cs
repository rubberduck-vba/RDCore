using RDCore.SDK.Runtime;
using System.Globalization;

namespace RDCore.Runtime.Semantics.Conversion;

/// <summary>
/// How a <c>String</c> reads as a number when it is Let-coerced to one (<strong>MS-VBAL §5.5.1.2.4</strong>): a <c>numeric-coercion-string</c>, written by
/// the regional settings (<see cref="RegionalSettings"/>), or a hexadecimal or octal literal.
/// </summary>
/// <remarks>
/// What MS-VBA reads, recorded in Excel - which is more than the grammar of §5.5.1.2.4 says:
/// <list type="bullet">
/// <item><c>&amp;H</c> or <c>&amp;O</c> and its digits, in either case, with no sign and no type character; what it is depends on the type it is
/// converted to (<see cref="Reading.Radix"/>).</item>
/// <item>A sign before the number or after it (<c>1-</c>), or parentheses around it (<c>(1)</c>), which make it negative - never two of them.</item>
/// <item>The currency symbol before the number or after it, the sign on either side of it (<c>-$1</c>, <c>$-1</c>).</item>
/// <item>The digit grouping symbol anywhere after the first digit, even after the decimal symbol (<c>1,000.5,0</c> is 1000.5), and ignored.</item>
/// <item>An exponent, <c>e</c> or <c>d</c>, after all of it (<c>$1e3</c>), and white space around the parts.</item>
/// </list>
/// </remarks>
internal static class NumberGrammar
{
    /// <summary>
    /// What a string reads as: the bits of a hexadecimal or octal literal, or a decimal number.
    /// </summary>
    /// <param name="Radix">The value of the digits of a <c>&amp;H</c> or <c>&amp;O</c> literal, unsigned: up to 32 bits is the <c>Long</c> they are (and up to
    /// 16 an <c>Integer</c>), a negative one when the high bit is set; more than that is the number they are.</param>
    /// <param name="Digits">Where the digits of a decimal number are in the buffer given to <see cref="TryRead"/>, with <c>.</c> before its fraction, its sign
    /// in front: what <see cref="decimal"/> and <see cref="double"/> read invariantly.</param>
    /// <param name="Exponent">The power of ten the number is multiplied by, when it is written with one.</param>
    public readonly record struct Reading(ulong? Radix, Range Digits, int? Exponent);

    /// <summary>
    /// The length of the buffer <see cref="TryRead"/> writes the digits of <paramref name="text"/> into: one more than the text, for the sign.
    /// </summary>
    /// <param name="text">The text to read.</param>
    /// <returns>The buffer's length.</returns>
    public static int DigitsLengthOf(ReadOnlySpan<char> text) => text.Length + 1;

    /// <summary>
    /// Reads <paramref name="text"/> as a number.
    /// </summary>
    /// <param name="text">The text to read.</param>
    /// <param name="regional">The regional settings the number is written by.</param>
    /// <param name="digits">Where the digits of a decimal number are written, at least <see cref="DigitsLengthOf"/> long.</param>
    /// <param name="reading">What the text reads as.</param>
    /// <returns><see langword="false"/> when it is not one; <see langword="true"/> with a <paramref name="reading"/> whose radix literal has too many
    /// digits for 64 bits is never returned: such a string is not a number either.</returns>
    public static bool TryRead(ReadOnlySpan<char> text, RegionalSettings regional, Span<char> digits, out Reading reading)
    {
        reading = default;
        var trimmed = text.Trim();
        return trimmed.Length > 0 && (TryReadRadix(trimmed, out reading) || TryReadDecimal(trimmed, regional, digits, out reading));
    }

    private static bool TryReadRadix(ReadOnlySpan<char> text, out Reading reading)
    {
        reading = default;
        if (text.Length < 3 || text[0] != '&' || char.ToUpperInvariant(text[1]) is not ('H' or 'O'))
        {
            return false;
        }

        var isHex = char.ToUpperInvariant(text[1]) == 'H';
        ulong value = 0;
        foreach (var c in text[2..])
        {
            var digit = isHex ? HexDigit(c) : c is >= '0' and <= '7' ? c - '0' : -1;
            var radix = isHex ? 16UL : 8UL;
            if (digit < 0 || value > (ulong.MaxValue - (ulong)digit) / radix)
            {
                return false;
            }

            value = value * radix + (ulong)digit;
        }

        reading = new Reading(value, default, null);
        return true;
    }

    private static int HexDigit(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => -1,
    };

    private static bool TryReadDecimal(ReadOnlySpan<char> text, RegionalSettings regional, Span<char> digits, out Reading reading)
    {
        reading = default;
        var at = 0;
        var negative = false;
        var signs = 0;
        var currency = false;

        Skip(text, ref at);
        var parenthesized = At(text, at, "(");
        if (parenthesized)
        {
            at++;
            Skip(text, ref at);
        }

        // what may come before the number: a sign and a currency symbol, in either order.
        for (var round = 0; round < 2; round++)
        {
            if (TrySign(text, ref at, ref negative))
            {
                signs++;
                Skip(text, ref at);
            }
            else if (!currency && TryTake(text, ref at, regional.CurrencySymbol))
            {
                currency = true;
                Skip(text, ref at);
            }
        }

        // the digits go after the sign's place, which is filled once the sign is known: it may come after them.
        if (signs > 1 || !TryReadMantissa(text, ref at, regional, digits[1..], out var written))
        {
            return false;
        }

        if (!TryReadExponent(text, ref at, out var exponent))
        {
            return false;
        }

        // what may come after it: a currency symbol and a sign, in either order.
        for (var round = 0; round < 2; round++)
        {
            Skip(text, ref at);
            if (signs == 0 && TrySign(text, ref at, ref negative))
            {
                signs++;
            }
            else if (!currency && TryTake(text, ref at, regional.CurrencySymbol))
            {
                currency = true;
            }
        }

        Skip(text, ref at);
        if (parenthesized)
        {
            // parentheses are the sign of the number: a number in them has no other.
            if (signs > 0 || !At(text, at, ")"))
            {
                return false;
            }

            negative = true;
            at++;
            Skip(text, ref at);
        }

        if (at != text.Length)
        {
            return false;
        }

        digits[0] = '-';
        reading = new Reading(null, negative ? 0..(written + 1) : 1..(written + 1), exponent);
        return true;
    }

    // digits, the grouping symbol after the first of them, and the decimal symbol once: the digits, with '.' for the decimal symbol, and how many there are.
    private static bool TryReadMantissa(ReadOnlySpan<char> text, ref int at, RegionalSettings regional, Span<char> digits, out int written)
    {
        written = 0;
        var seenDigit = false;
        var seenDecimal = false;
        while (at < text.Length)
        {
            if (char.IsAsciiDigit(text[at]))
            {
                digits[written++] = text[at++];
                seenDigit = true;
            }
            else if (seenDigit && TryTake(text, ref at, regional.DigitGroupingSymbol))
            {
                // ignored: grouping is for the reader.
            }
            else if (!seenDecimal && TryTake(text, ref at, regional.DecimalSymbol))
            {
                digits[written++] = '.';
                seenDecimal = true;
            }
            else
            {
                break;
            }
        }

        return seenDigit;
    }

    // ("e" / "d") [sign] digits, with white space around its parts.
    private static bool TryReadExponent(ReadOnlySpan<char> text, ref int at, out int? exponent)
    {
        exponent = null;
        var start = at;
        Skip(text, ref at);
        if (at >= text.Length || char.ToLowerInvariant(text[at]) is not ('e' or 'd'))
        {
            at = start;
            return true;
        }

        at++;
        Skip(text, ref at);
        var negative = false;
        TrySign(text, ref at, ref negative);
        Skip(text, ref at);
        var digitsStart = at;
        while (at < text.Length && char.IsAsciiDigit(text[at]))
        {
            at++;
        }

        if (at == digitsStart || !int.TryParse(text[digitsStart..at], NumberStyles.None, CultureInfo.InvariantCulture, out var power))
        {
            return false;
        }

        exponent = negative ? -power : power;
        return true;
    }

    private static bool TrySign(ReadOnlySpan<char> text, ref int at, ref bool negative)
    {
        if (at < text.Length && text[at] is '+' or '-')
        {
            negative = text[at] == '-';
            at++;
            return true;
        }

        return false;
    }

    private static bool TryTake(ReadOnlySpan<char> text, ref int at, string symbol)
    {
        if (symbol.Length > 0 && At(text, at, symbol))
        {
            at += symbol.Length;
            return true;
        }

        return false;
    }

    private static bool At(ReadOnlySpan<char> text, int at, string symbol) => text[at..].StartsWith(symbol, StringComparison.Ordinal);

    private static void Skip(ReadOnlySpan<char> text, ref int at)
    {
        while (at < text.Length && char.IsWhiteSpace(text[at]))
        {
            at++;
        }
    }
}
