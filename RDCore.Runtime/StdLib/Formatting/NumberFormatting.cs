using System.Globalization;
using System.Runtime.CompilerServices;

namespace RDCore.Runtime.StdLib.Formatting;

/// <summary>How <c>FormatNumber</c>, <c>FormatCurrency</c> and <c>FormatPercent</c> write a number.</summary>
internal enum NumberStyle
{
    Number,
    Currency,
    Percent,
}

/// <summary>
/// A number written by a section of a user-defined number format, or in the style of <c>FormatNumber</c>, <c>FormatCurrency</c> or
/// <c>FormatPercent</c>, in the regional settings of the session's environment.
/// </summary>
/// <remarks>
/// Every digit comes from <see cref="DecimalDigits"/>: a number is rounded half away from zero, on its decimal digits, so
/// <c>Format(2.5, "0")</c> is 3 and <c>Format(1.005, "0.00")</c> is 1.01, a Double being its 15 significant digits.
/// </remarks>
internal static class NumberFormatting
{
    /// <summary>
    /// 🎯 The places after the decimal separator <c>FormatNumber</c> and <c>FormatPercent</c> write when they are not told: 2, as
    /// the named formats <c>Fixed</c>, <c>Standard</c> and <c>Percent</c> do. The culture's <see cref="NumberFormatInfo.NumberDecimalDigits"/>
    /// is not read: ICU says 3 where Windows says 2 for the same locale, and a program writes the same number on every platform.
    /// </summary>
    public const int DefaultFractionDigits = 2;

    // a Decimal has 29 digits, and rounding can carry one more.
    private const int DigitCapacity = 32;

    private enum Token
    {
        Literal,
        Zero,
        Hash,
        Point,
        Comma,
        Percent,
        Exponent,
        DateSeparator,
        TimeSeparator,
    }

    private enum Part
    {
        Integer,
        Fraction,
        Exponent,
    }

    // what a section asks of a number, read before any of it is written.
    private readonly record struct Layout(
        int IntegerPlaceholders,
        int MinimumIntegerDigits,
        int FractionPlaceholders,
        int MinimumFractionDigits,
        bool Grouped,
        int Thousands,
        int Percents,
        bool Scientific,
        int MinimumExponentDigits);

    /// <summary>
    /// Writes <paramref name="number"/> by a number section: the digits of its magnitude, with a minus sign before everything when
    /// <paramref name="signed"/> (a negative number written by the first section).
    /// </summary>
    public static void WriteSection(ref TextBuilder output, ReadOnlySpan<char> section, in NumberValue number, bool signed, in FormatSettings settings)
    {
        var layout = LayoutOf(section);
        Span<byte> buffer = stackalloc byte[DigitCapacity];
        var digits = new DecimalDigits(buffer);
        number.WriteTo(ref digits);

        // "%" multiplies by 100 each, and a thousand separator that ends the whole part divides by 1000 each: "##0,," writes millions.
        digits.Scale(2 * layout.Percents - 3 * layout.Thousands);

        var exponent = 0;
        int integerDigits;
        if (layout.Scientific)
        {
            // as many digits before the point as there are placeholders for them; rounding can carry the mantissa to one more.
            var wanted = Math.Max(1, layout.IntegerPlaceholders);
            exponent = digits.Normalize(wanted);
            digits.Round(layout.FractionPlaceholders);
            exponent += digits.Normalize(wanted);
            integerDigits = Math.Max(digits.IntegerDigits, layout.MinimumIntegerDigits);
        }
        else
        {
            digits.Round(layout.FractionPlaceholders);
            integerDigits = Math.Max(digits.IntegerDigits, layout.MinimumIntegerDigits);
        }

        var fractionDigits = Math.Max(layout.MinimumFractionDigits, Math.Min(layout.FractionPlaceholders, digits.FractionDigits));
        var numbers = settings.Numbers;
        var groups = new Grouping(layout.Grouped ? SizesOf(numbers).Number : [], numbers.NumberGroupSeparator);

        if (signed)
        {
            output.Append('-');
        }

        var part = Part.Integer;
        var integerPlaceholder = 0;
        var fractionPlaceholder = 0;
        var exponentWritten = false;
        var index = 0;
        while (index < section.Length)
        {
            switch (Next(section, ref index, out var literal))
            {
                case Token.Literal:
                    output.Append(literal);
                    break;
                case Token.Zero or Token.Hash when part is Part.Integer:
                    if (integerPlaceholder == 0)
                    {
                        // digits the placeholders have no room for are written at the first of them.
                        for (var position = 0; position < integerDigits - layout.IntegerPlaceholders; position++)
                        {
                            WriteWholeDigit(ref output, WholeDigit(digits, position, integerDigits), integerDigits - 1 - position, groups);
                        }
                    }

                    var placed = integerDigits - layout.IntegerPlaceholders + integerPlaceholder++;
                    if (placed >= 0)
                    {
                        WriteWholeDigit(ref output, WholeDigit(digits, placed, integerDigits), integerDigits - 1 - placed, groups);
                    }

                    break;
                case Token.Zero or Token.Hash when part is Part.Fraction:
                    if (fractionPlaceholder < fractionDigits)
                    {
                        output.Append((char)('0' + digits.FractionDigit(fractionPlaceholder)));
                    }

                    fractionPlaceholder++;
                    break;
                case Token.Zero or Token.Hash:
                    if (!exponentWritten)
                    {
                        output.AppendNumber(Math.Abs(exponent), Math.Max(1, layout.MinimumExponentDigits));
                        exponentWritten = true;
                    }

                    break;
                case Token.Point when part is Part.Integer:
                    if (layout.IntegerPlaceholders == 0)
                    {
                        // ".00" has no placeholder for the whole part, which is written before the separator all the same.
                        for (var position = 0; position < integerDigits; position++)
                        {
                            WriteWholeDigit(ref output, WholeDigit(digits, position, integerDigits), integerDigits - 1 - position, groups);
                        }
                    }

                    output.Append(numbers.NumberDecimalSeparator);
                    part = Part.Fraction;
                    break;
                case Token.Point:
                    output.Append('.');
                    break;
                case Token.Comma:
                    break;
                case Token.Percent:
                    output.Append('%');
                    break;
                case Token.Exponent when layout.Scientific && part is not Part.Exponent:
                    output.Append(literal[0]);
                    if (exponent < 0)
                    {
                        output.Append('-');
                    }
                    else if (literal[1] == '+')
                    {
                        output.Append('+');
                    }

                    part = Part.Exponent;
                    break;
                case Token.Exponent:
                    output.Append(literal);
                    break;
                case Token.DateSeparator:
                    output.Append(settings.Dates.DateSeparator);
                    break;
                case Token.TimeSeparator:
                    output.Append(settings.Dates.TimeSeparator);
                    break;
            }
        }
    }

    /// <summary>
    /// Writes <paramref name="number"/> as <c>FormatNumber</c>, <c>FormatCurrency</c> or <c>FormatPercent</c> do: rounded to
    /// <paramref name="places"/> after the decimal separator (the style's own when it is -1), a fraction with or without its leading
    /// zero, digits grouped or not, and a negative number in parentheses or with a minus sign, each as it is told or, for
    /// <see langword="null"/>, as the culture says.
    /// </summary>
    public static void WriteStyled(ref TextBuilder output, in NumberValue number, NumberStyle style, int places, bool? leadingDigit, bool? parentheses, bool? groupDigits, in FormatSettings settings)
    {
        var numbers = settings.Numbers;
        var isCurrency = style is NumberStyle.Currency;
        places = places >= 0 ? places : isCurrency ? numbers.CurrencyDecimalDigits : DefaultFractionDigits;

        Span<byte> buffer = stackalloc byte[DigitCapacity];
        var digits = new DecimalDigits(buffer);
        number.WriteTo(ref digits);
        if (style is NumberStyle.Percent)
        {
            digits.Scale(2);
        }

        digits.Round(places);

        // 👉 a number that rounds to zero is written as zero, with no sign: "-0.00" and "($0.00)" are not amounts.
        var negative = number.Sign < 0 && !digits.IsZero;
        var sizes = isCurrency ? SizesOf(numbers).Currency : SizesOf(numbers).Number;
        var grouped = groupDigits ?? (sizes.Length > 0 && sizes[0] > 0);
        var groups = new Grouping(grouped ? sizes : [], isCurrency ? numbers.CurrencyGroupSeparator : numbers.NumberGroupSeparator);
        var separator = isCurrency ? numbers.CurrencyDecimalSeparator : numbers.NumberDecimalSeparator;

        var pattern = isCurrency
            ? negative ? CurrencyNegativePatterns[CurrencyNegativePattern(numbers, parentheses)] : CurrencyPositivePatterns[numbers.CurrencyPositivePattern]
            : negative ? NumberNegativePattern(numbers, parentheses) : "n";

        foreach (var character in pattern)
        {
            switch (character)
            {
                case 'n':
                    // the whole part (a zero for a fraction, if it is to have one) and exactly so many places of the fraction.
                    var integerDigits = digits.IntegerDigits;
                    if (integerDigits == 0 && (leadingDigit ?? true))
                    {
                        output.Append('0');
                    }

                    for (var position = 0; position < integerDigits; position++)
                    {
                        WriteWholeDigit(ref output, digits.IntegerDigit(position), integerDigits - 1 - position, groups);
                    }

                    if (places > 0)
                    {
                        output.Append(separator);
                        for (var place = 0; place < places; place++)
                        {
                            output.Append((char)('0' + digits.FractionDigit(place)));
                        }
                    }

                    if (style is NumberStyle.Percent)
                    {
                        output.Append('%');
                    }

                    break;
                case '$':
                    output.Append(CurrencySymbols.Of(settings.Culture));
                    break;
                default:
                    output.Append(character);
                    break;
            }
        }
    }

    // the digit at a position of the whole part as it is written, which has leading zeros where it has more digits than the number.
    private static int WholeDigit(scoped in DecimalDigits digits, int position, int written)
    {
        var leadingZeros = written - digits.IntegerDigits;
        return position < leadingZeros ? 0 : digits.IntegerDigit(position - leadingZeros);
    }

    // a digit of the whole part, then a group separator if the digits after it begin a group.
    private static void WriteWholeDigit(ref TextBuilder output, int digit, int digitsAfter, Grouping groups)
    {
        output.Append((char)('0' + digit));
        if (groups.EndsAGroup(digitsAfter))
        {
            output.Append(groups.Separator);
        }
    }

    private static Layout LayoutOf(ReadOnlySpan<char> section)
    {
        int integerPlaceholders = 0, firstZero = -1, fractionPlaceholders = 0, minimumFraction = 0;
        int exponentPlaceholders = 0, minimumExponent = 0, percents = 0, thousands = 0, pendingCommas = 0;
        bool grouped = false, exponent = false;
        var part = Part.Integer;
        var index = 0;
        while (index < section.Length)
        {
            switch (Next(section, ref index, out _))
            {
                case Token.Zero or Token.Hash when part is Part.Integer:
                    if (section[index - 1] == '0' && firstZero < 0)
                    {
                        firstZero = integerPlaceholders;
                    }

                    integerPlaceholders++;

                    // a thousand separator with a placeholder after it, in the whole part, groups the digits.
                    grouped |= pendingCommas > 0;
                    pendingCommas = 0;
                    break;
                case Token.Zero or Token.Hash when part is Part.Fraction:
                    fractionPlaceholders++;
                    if (section[index - 1] == '0')
                    {
                        minimumFraction = fractionPlaceholders;
                    }

                    break;
                case Token.Zero or Token.Hash:
                    exponentPlaceholders++;
                    if (section[index - 1] == '0')
                    {
                        minimumExponent++;
                    }

                    break;
                case Token.Comma when part is Part.Integer && integerPlaceholders > 0:
                    pendingCommas++;
                    break;
                case Token.Point when part is Part.Integer:
                    thousands = pendingCommas;
                    pendingCommas = 0;
                    part = Part.Fraction;
                    break;
                case Token.Percent:
                    percents++;
                    break;
                case Token.Exponent when !exponent:
                    exponent = true;
                    thousands = part is Part.Integer ? pendingCommas : thousands;
                    pendingCommas = 0;
                    part = Part.Exponent;
                    break;
            }
        }

        if (part is Part.Integer)
        {
            // and one that ends it, "immediately to the left of the decimal separator (whether or not a decimal is specified)", scales.
            thousands = pendingCommas;
        }

        // "if the format expression contains at least one digit placeholder (0 or #) to the right of E-, E+, e-, or e+".
        var scientific = exponent && exponentPlaceholders > 0;
        return new Layout(
            integerPlaceholders,
            firstZero < 0 ? 0 : integerPlaceholders - firstZero,
            fractionPlaceholders,
            minimumFraction,
            grouped,
            thousands,
            percents,
            scientific,
            minimumExponent);
    }

    private static Token Next(ReadOnlySpan<char> section, ref int index, out ReadOnlySpan<char> literal)
    {
        if (FormatSections.TryReadLiteral(section, ref index, out literal))
        {
            return Token.Literal;
        }

        var character = section[index];
        if (character is 'E' or 'e' && FormatSections.IsExponent(section, index))
        {
            literal = section.Slice(index, 2);
            index += 2;
            return Token.Exponent;
        }

        literal = section.Slice(index++, 1);
        return character switch
        {
            '0' => Token.Zero,
            '#' => Token.Hash,
            '.' => Token.Point,
            ',' => Token.Comma,
            '%' => Token.Percent,
            '/' => Token.DateSeparator,
            ':' => Token.TimeSeparator,
            _ => Token.Literal,
        };
    }

    // NumberFormatInfo.NumberNegativePattern: "(n)", "-n", "- n", "n-", "n -". With parentheses asked for, "(n)"; refused, the
    // culture's own pattern unless that is "(n)", which is "-n" instead.
    private static string NumberNegativePattern(NumberFormatInfo numbers, bool? parentheses)
        => (parentheses ?? numbers.NumberNegativePattern == 0) ? "(n)" : numbers.NumberNegativePattern switch
        {
            2 => "- n",
            3 => "n-",
            4 => "n -",
            _ => "-n",
        };

    private static readonly string[] CurrencyPositivePatterns = ["$n", "n$", "$ n", "n $"];

    private static readonly string[] CurrencyNegativePatterns =
    [
        "($n)", "-$n", "$-n", "$n-", "(n$)", "-n$", "n-$", "n$-", "-n $", "-$ n", "n $-", "$ n-", "$ -n", "n- $", "($ n)", "(n $)", "$- n",
    ];

    // the culture's negative currency pattern, unless it is told otherwise: parentheses where the symbol is where the positive pattern
    // puts it, or no parentheses with a minus sign before the amount and its symbol.
    private static int CurrencyNegativePattern(NumberFormatInfo numbers, bool? parentheses)
    {
        var pattern = numbers.CurrencyNegativePattern;
        var hasParentheses = pattern is 0 or 4 or 14 or 15;
        return parentheses switch
        {
            true when !hasParentheses => numbers.CurrencyPositivePattern switch { 1 => 4, 2 => 14, 3 => 15, _ => 0 },
            false when hasParentheses => pattern switch { 4 => 5, 14 => 9, 15 => 8, _ => 1 },
            _ => pattern,
        };
    }

    // NumberFormatInfo copies its group sizes for every read of them: these are read once per culture.
    private static readonly ConditionalWeakTable<NumberFormatInfo, GroupSizes> Sizes = [];

    private sealed record class GroupSizes(int[] Number, int[] Currency);

    private static GroupSizes SizesOf(NumberFormatInfo numbers)
        => Sizes.GetValue(numbers, static numbers => new GroupSizes(numbers.NumberGroupSizes, numbers.CurrencyGroupSizes));

    // the culture's digit groups: NumberFormatInfo's sizes, from the decimal separator leftward, the last repeating unless it is 0.
    private readonly struct Grouping(int[] sizes, string separator)
    {
        public string Separator { get; } = separator;

        // whether a separator goes before the last digitsAfter digits of the whole part.
        public bool EndsAGroup(int digitsAfter)
        {
            if (digitsAfter <= 0 || sizes.Length == 0)
            {
                return false;
            }

            var boundary = 0;
            for (var group = 0; ; group++)
            {
                var size = sizes[Math.Min(group, sizes.Length - 1)];
                if (size <= 0)
                {
                    return false;
                }

                boundary += size;
                if (boundary >= digitsAfter)
                {
                    return boundary == digitsAfter;
                }
            }
        }
    }
}
