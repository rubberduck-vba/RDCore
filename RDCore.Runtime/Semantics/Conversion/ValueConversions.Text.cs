using RDCore.SDK.Model;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using System.Globalization;

namespace RDCore.Runtime.Semantics.Conversion;

public static partial class ValueConversions
{
    /// <summary>
    /// Converts <paramref name="source"/> to a <c>String</c> (<strong>MS-VBAL §5.5.1.2.4</strong>), from a
    /// <c>String</c>, any numeric type, <c>Boolean</c>, <c>Date</c>, <c>Empty</c> (<strong>§5.5.1.2.11</strong>),
    /// or <c>Byte()</c> (<strong>§5.5.1.2.6</strong>).
    /// </summary>
    /// <remarks>
    /// A number is written with the regional decimal symbol, and a date in the regional Short Date and Long Time
    /// formats: §5.5.1.2.4's "host-defined regional settings", which are <paramref name="culture"/>'s. The thread's
    /// own culture is never read.
    /// </remarks>
    /// <param name="source">The value, unwrapped from any <c>Variant</c> that held it.</param>
    /// <param name="culture">The regional settings to write a number or a date in; the invariant culture when none is given.</param>
    public static ValueConversionResult ToText(VBTypedValue source, CultureInfo? culture = null) => source switch
    {
        // result is a copy of the source string: its runtime value, which is immutable, so the copy has every byte of
        // its data - a trailing byte included - and a copy of vbNullString is vbNullString.
        VBStringValue text => ValueConversionResult.Success(new VBStringValue(new ValueBindingHandle(text.RuntimeValue))),

        VBNumericTypedValue number => ValueConversionResult.Success(new VBStringValue(NumberToText(number, culture ?? CultureInfo.InvariantCulture))),

        VBBooleanValue boolean => ValueConversionResult.Success(new VBStringValue((bool)boolean.Value ? Tokens.True : Tokens.False)),

        VBDateValue date => ValueConversionResult.Success(new VBStringValue(FormatDate(date.Value, culture ?? CultureInfo.InvariantCulture))),

        // MS-VBAL 5.5.1.2.11: "The result is a 0-length string."
        VBEmptyValue => ValueConversionResult.Success(VBStringValue.ZeroLengthString),

        // MS-VBAL 5.5.1.2.6: "The binary data within the source Byte array is interpreted as if it represents the
        // implementation-defined binary format used to store String data [...] This coercion never raises a runtime
        // error. If the byte array is uninitialized, the result is a 0-length string." RDCore's format is UTF-16LE code
        // units and, when the data is an odd number of bytes, the byte left over after them, so every byte array is one
        // and every byte of it is kept: the specification's "?" for data that "cannot be represented on the current
        // platform", and its discarded "trailing bytes leftover", describe formats that cannot hold some of them.
        VBArrayValue { ItemType: VBByteType } bytes
            => ValueConversionResult.Success(VBStringValue.FromBytes(VBByteArrayCoercionHelpers.Flatten(bytes))),

        VBNullValue => ValueConversionResult.Failure(VBRuntimeErrorId.InvalidUseOfNull),

        // MS-VBAL 5.5.1.2.9: an Error converts to nothing but a Variant or an Error.
        VBErrorValue => ValueConversionResult.Failure(VBRuntimeErrorId.TypeMismatch),

        _ => ValueConversionResult.NotApplicable,
    };

    // MS-VBAL 5.5.1.2.4: if the day value of the source date is 12/30/1899, only the date's time is
    // converted (Long Time format) — compares DateTime.Date rather than requiring the exact zero serial value,
    // since every time of day on 12/30/1899 is itself a "day value" of 12/30/1899. Otherwise the source date's
    // full date and time value is converted (Short Date format); when the time-of-day is exactly midnight that
    // full value has nothing to add, so only the date prints — real VBA drops a trailing "12:00:00 AM" from a
    // date-only value.
    private static string FormatDate(DateTime value, CultureInfo culture)
    {
        var format = DateFormatOf(culture);
        return value.Date == VBDateType.Zero.Value.Date
            ? value.ToString("T", format)
            : value.TimeOfDay == TimeSpan.Zero
                ? value.ToString("d", format)
                : $"{value.ToString("d", format)} {value.ToString("T", format)}";
    }

    // the formats of the culture, in the Gregorian calendar whatever calendar the culture writes in by default: MS-VBA's
    // Calendar setting is "vbCalGreg 0 Use Gregorian calendar (default)", and MS-VBAL 6.1.2.4.2.1 gives the setting (the
    // session's ISessionClock.Calendar) to "subsequent calls to the functions defined in section 6.1.2.4" only, so a
    // let-coercion writes in the Gregorian calendar whatever it is: under th-TH a date is written with th-TH's patterns and
    // 2020 is 2020, not 2563. A culture that offers no Gregorian calendar at all keeps its own.
    private static DateTimeFormatInfo DateFormatOf(CultureInfo culture)
    {
        if (culture.Calendar is GregorianCalendar
            || culture.OptionalCalendars.OfType<GregorianCalendar>().FirstOrDefault() is not { } gregorian)
        {
            return culture.DateTimeFormat;
        }

        var format = (DateTimeFormatInfo)culture.DateTimeFormat.Clone();
        format.Calendar = gregorian;
        return format;
    }

    private static string NumberToText(VBNumericTypedValue value, CultureInfo cultureInfo)
    {
        // MS-VBAL 5.5.1.2.4: the maximum number of integral significant figures of "any integral or fixed-point type" is infinite - normal notation, with
        // every digit of the integer part, which a Double does not have of a LongLong past 2^53, nor of a Decimal.
        if (WholeValueOf(value) is { } whole)
        {
            return whole.ToString(CultureInfo.InvariantCulture);
        }

        if (FixedValueOf(value) is { } fixedPoint)
        {
            return FixedPointToText(fixedPoint, cultureInfo.NumberFormat.NumberDecimalSeparator);
        }

        // BoxedValue's CLR type tracks the source's own numeric type (int for Long, short for
        // Integer, ...), not always double — a direct (double) unboxing cast throws for anything
        // that isn't already a boxed double. AsDouble widens through Convert.ToDouble instead.
        var numericValue = value.AsDouble;
        if (numericValue == 0)
        {
            return VBStringValue.Zero;
        }

        if (double.IsPositiveInfinity(numericValue))
        {
            return VBStringValue.PositiveInfinity;
        }

        if (double.IsNegativeInfinity(numericValue))
        {
            return VBStringValue.NegativeInfinity;
        }

        if (double.IsNaN(numericValue))
        {
            return VBStringValue.NaN;
        }

        var absoluteValue = Math.Abs(numericValue);
        var dot = cultureInfo.NumberFormat.NumberDecimalSeparator;

        // MS-VBAL 5.5.1.2.4: scientific notation is used whenever the integer part has more than the
        // source type's maximum significant integral digits, regardless of whether the value also has
        // a fractional part. "F0" (fixed-point, no fractional digits) reliably yields just the integer
        // part without .NET's default ToString() collapsing a very large/small magnitude into its own
        // scientific notation first (which would otherwise hide the decimal separator this method used
        // to gate on, undercounting whole-number values entirely).
        var integerPartDigitCount = absoluteValue < 1 ? 1 : absoluteValue.ToString("F0", cultureInfo).Length;
        var significantIntegerDigits = value is VBSingleValue
            ? VBSingleType.SignificantIntegerDigits
            : VBDoubleType.SignificantIntegerDigits;

        return integerPartDigitCount > significantIntegerDigits
            ? ToScientificNotation(numericValue, significantIntegerDigits, dot)
            : ToNormalNotation(numericValue, significantIntegerDigits, dot);
    }

    // MS-VBAL 5.5.1.2.4, for a Currency or a Decimal: every digit of the integer part, then "as many digits as possible of the fractional part of the
    // number such that a maximum of 15 integer and fractional digits are printed total with trailing zeros removed" - counted, for a number with no integer
    // part, from its first digit that is not 0, as a Double's are. The last digit is rounded half away from zero, as a Double's is.
    private static string FixedPointToText(decimal value, string decimalSeparator)
    {
        const int maximumDigits = 15;
        if (value == 0)
        {
            return VBStringValue.Zero;
        }

        var absoluteValue = Math.Abs(value);
        var integerPart = decimal.Truncate(absoluteValue);
        int fractionalDigits;
        if (integerPart > 0)
        {
            fractionalDigits = Math.Max(0, maximumDigits - integerPart.ToString(CultureInfo.InvariantCulture).Length);
        }
        else
        {
            var leadingZeros = 0;
            for (var scaled = absoluteValue * 10; scaled < 1; scaled *= 10)
            {
                leadingZeros++;
            }

            fractionalDigits = Math.Min(28, leadingZeros + maximumDigits);
        }

        var rounded = Math.Round(absoluteValue, fractionalDigits, MidpointRounding.AwayFromZero);
        var digits = rounded.ToString(fractionalDigits == 0 ? "0" : $"0.{new string('#', fractionalDigits)}", CultureInfo.InvariantCulture);
        return $"{(value < 0 ? "-" : string.Empty)}{digits.Replace(".", decimalSeparator)}";
    }

    // MS-VBAL 5.5.1.2.4: "as many digits as possible of the fractional part of the number such that a
    // maximum of [15, or 7 for Single] integer and fractional digits are printed total with trailing
    // zeros removed" — double's own default formatting prints its shortest round-trippable
    // representation instead (up to 17 significant digits), so CStr(0.1 + 0.2) would otherwise
    // read "0.30000000000000004" instead of "0.3".
    private static string ToNormalNotation(double value, int significantIntegerDigits, string decimalSeparator)
    {
        var sign = value < 0 ? "-" : string.Empty;
        var absoluteValue = Math.Abs(value);

        // decompose into a rounded significand + decimal exponent the same way ToScientificNotation
        // does (same total-digit cap), then reassemble as fixed-point instead of "dEe" notation.
        var maxFractionalDigits = Math.Max(0, significantIntegerDigits - 1);
        var scientific = absoluteValue.ToString($"E{maxFractionalDigits}", CultureInfo.InvariantCulture);
        var eIndex = scientific.IndexOf('E');
        var exponent = int.Parse(scientific[(eIndex + 1)..], CultureInfo.InvariantCulture);
        var leadingDigit = scientific[0];
        var fractionalDigits = scientific[2..eIndex];
        var digits = $"{leadingDigit}{fractionalDigits}".TrimEnd('0');
        if (digits.Length == 0)
        {
            digits = "0";
        }

        // exponent is the power of ten of the leading digit; the decimal point falls exponent+1
        // digits into "digits" (negative exponent: the value is entirely a fractional leading-zero run).
        string integerPart, fractionalPart;
        if (exponent >= 0 && digits.Length <= exponent + 1)
        {
            integerPart = digits.PadRight(exponent + 1, '0');
            fractionalPart = string.Empty;
        }
        else if (exponent >= 0)
        {
            integerPart = digits[..(exponent + 1)];
            fractionalPart = digits[(exponent + 1)..];
        }
        else
        {
            integerPart = "0";
            fractionalPart = new string('0', -exponent - 1) + digits;
        }

        return fractionalPart.Length > 0
            ? $"{sign}{integerPart}{decimalSeparator}{fractionalPart}"
            : $"{sign}{integerPart}";
    }

    private static string ToScientificNotation(double value, int significantIntegerDigits, string decimalSeparator)
    {
        var sign = value < 0 ? "-" : string.Empty;
        var absoluteValue = Math.Abs(value);

        // .NET's own "E" format always yields exactly one digit before the decimal point (s * 10^e),
        // regardless of the source's magnitude or whether it has a fractional part — reliably giving
        // the significand and exponent without needing to first locate a decimal separator that may
        // not even be present (a whole-number source) or that .NET's default ToString() may already
        // have collapsed into its own scientific notation.
        //
        // MS-VBAL 5.5.1.2.4: "a maximum of 15 [7 for Single] integer and significand digits are
        // printed total with trailing zeros removed" — the leading digit `s` counts as one of them, so
        // the format asks for one fewer fractional digit. Asking "E" for exactly the capped count
        // *rounds* the significand there; slicing the digits of a wider "E16" truncates instead, and
        // the two differ for every value whose binary representation sits just below its decimal one
        // (0.3 is stored as 0.29999999999999998..., whose leading 15 significant digits are
        // "299999999999999" where VBA prints "0.3").
        var maxFractionalDigits = Math.Max(0, significantIntegerDigits - 1);
        var scientific = absoluteValue.ToString($"E{maxFractionalDigits}", CultureInfo.InvariantCulture);
        var eIndex = scientific.IndexOf('E');
        var exponent = int.Parse(scientific[(eIndex + 1)..], CultureInfo.InvariantCulture);
        var s = scientific[0];

        var fractionalDigits = scientific[2..eIndex].TrimEnd('0');

        // MS-VBAL 5.5.1.2.4: the exponent is always signed ("+" or "-"), unlike a bare C# int.ToString().
        var exponentSign = exponent < 0 ? "-" : "+";
        return fractionalDigits.Length > 0
            ? $"{sign}{s}{decimalSeparator}{fractionalDigits}E{exponentSign}{Math.Abs(exponent)}"
            : $"{sign}{s}E{exponentSign}{Math.Abs(exponent)}";
    }
}
