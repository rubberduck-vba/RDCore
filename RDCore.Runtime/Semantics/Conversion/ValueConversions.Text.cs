using RDCore.SDK.Model;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using System.Globalization;
using System.Text;

namespace RDCore.Runtime.Semantics.Conversion;

public static partial class ValueConversions
{
    /// <summary>
    /// Converts <paramref name="source"/> to a <c>String</c> (<strong>MS-VBAL §5.5.1.2.4</strong>), from a
    /// <c>String</c>, any numeric type, <c>Boolean</c>, <c>Date</c>, <c>Empty</c> (<strong>§5.5.1.2.11</strong>),
    /// or <c>Byte()</c> (<strong>§5.5.1.2.6</strong>).
    /// </summary>
    /// <param name="source">The value, unwrapped from any <c>Variant</c> that held it.</param>
    public static ValueConversionResult ToText(VBTypedValue source) => source switch
    {
        // result is a copy of the source string.
        VBStringValue text => ValueConversionResult.Success(new VBStringValue(text.Value)),

        VBNumericTypedValue number => ValueConversionResult.Success(new VBStringValue(NumberToText(number))),

        VBBooleanValue boolean => ValueConversionResult.Success(new VBStringValue((bool)boolean.Value ? Tokens.True : Tokens.False)),

        VBDateValue date => ValueConversionResult.Success(new VBStringValue(FormatDate(date.Value))),

        // MS-VBAL 5.5.1.2.11: "The result is a 0-length string."
        VBEmptyValue => ValueConversionResult.Success(VBStringValue.ZeroLengthString),

        VBArrayValue { ItemType: VBByteType } bytes => ValueConversionResult.Success(new VBStringValue(FromBytes(bytes))),

        VBNullValue => ValueConversionResult.Failure(VBRuntimeErrorId.InvalidUseOfNull),

        _ => ValueConversionResult.NotApplicable,
    };

    // MS-VBAL 5.5.1.2.6: "The binary data within the source Byte array is interpreted as if it
    // represents the implementation-defined binary format used to store String data [...] The result
    // is the string produced. This coercion never raises a runtime error. If the byte array is
    // uninitialized, the result is a 0-length string. [...] Any trailing bytes leftover at the end of
    // the byte array that could not be interpreted are discarded." .NET's own String storage already
    // is that binary format (UTF-16LE), and every 2-byte pairing is a valid .NET char - the spec's "?"
    // fallback for a byte sequence that "cannot be represented on the current platform" describes other
    // hosts whose internal string encoding isn't UTF-16; it has no unrepresentable case to fall back to
    // here, so Encoding.Unicode.GetString (dropping an unpaired trailing byte) satisfies the rule as-is.
    private static string FromBytes(VBArrayValue byteArraySource)
    {
        var bytes = VBByteArrayCoercionHelpers.Flatten(byteArraySource);
        return Encoding.Unicode.GetString(bytes[..(bytes.Length - bytes.Length % 2)]);
    }

    // MS-VBAL 5.5.1.2.4: if the day value of the source date is 12/30/1899, only the date's time is
    // converted (Long Time format) — compares DateTime.Date rather than requiring the exact zero serial value,
    // since every time of day on 12/30/1899 is itself a "day value" of 12/30/1899. Otherwise the source date's
    // full date and time value is converted (Short Date format); when the time-of-day is exactly midnight that
    // full value has nothing to add, so only the date prints — real VBA drops a trailing "12:00:00 AM" from a
    // date-only value.
    private static string FormatDate(DateTime value) => value.Date == VBDateType.Zero.Value.Date
        ? value.ToLongTimeString()
        : value.TimeOfDay == TimeSpan.Zero
            ? value.ToShortDateString()
            : $"{value.ToShortDateString()} {value.ToLongTimeString()}";

    private static string NumberToText(VBNumericTypedValue value)
    {
        var cultureInfo = CultureInfo.InvariantCulture;

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
        var scientific = absoluteValue.ToString("E16", CultureInfo.InvariantCulture);
        var eIndex = scientific.IndexOf('E');
        var exponent = int.Parse(scientific[(eIndex + 1)..], CultureInfo.InvariantCulture);
        var leadingDigit = scientific[0];
        var fractionalDigits = scientific[2..eIndex];
        var maxFractionalDigits = Math.Max(0, significantIntegerDigits - 1);
        if (fractionalDigits.Length > maxFractionalDigits)
        {
            fractionalDigits = fractionalDigits[..maxFractionalDigits];
        }

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
        // have collapsed into its own scientific notation. 16 fractional digits comfortably covers a
        // double's ~15-17 significant decimal digits of precision.
        var scientific = absoluteValue.ToString("E16", CultureInfo.InvariantCulture);
        var eIndex = scientific.IndexOf('E');
        var exponent = int.Parse(scientific[(eIndex + 1)..], CultureInfo.InvariantCulture);
        var s = scientific[0];

        // MS-VBAL 5.5.1.2.4: "a maximum of 15 [7 for Single] integer and significand digits are
        // printed total with trailing zeros removed" — the leading digit `s` counts as one of them.
        var fractionalDigits = scientific[2..eIndex].TrimEnd('0');
        var maxFractionalDigits = Math.Max(0, significantIntegerDigits - 1);
        if (fractionalDigits.Length > maxFractionalDigits)
        {
            fractionalDigits = fractionalDigits[..maxFractionalDigits];
        }

        // MS-VBAL 5.5.1.2.4: the exponent is always signed ("+" or "-"), unlike a bare C# int.ToString().
        var exponentSign = exponent < 0 ? "-" : "+";
        return fractionalDigits.Length > 0
            ? $"{sign}{s}{decimalSeparator}{fractionalDigits}E{exponentSign}{Math.Abs(exponent)}"
            : $"{sign}{s}E{exponentSign}{Math.Abs(exponent)}";
    }
}
