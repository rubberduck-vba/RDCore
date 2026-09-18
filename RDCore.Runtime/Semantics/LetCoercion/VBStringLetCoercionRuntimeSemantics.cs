using RDCore.Runtime.Semantics.Abstract;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Context.Abstract;
using RDCore.SDK.Services.VerboseMessages;
using System.Globalization;
using System.Text;

namespace RDCore.Runtime.Semantics.LetCoercion;

/// <summary>
/// MS-VBAL 5.5.1.2.4 Let-coercion to and from <c>VBStringType</c>
/// </summary>
/// <remarks>
/// The coercion provider dispatches by <em>destination</em> type only, so this class — registered for
/// <see cref="VBStringType"/> — only ever runs when the destination actually is String; it therefore
/// implements the "-&gt; String" half of MS-VBAL 5.5.1.2.4's table (String, any numeric type, Boolean
/// or Date source). The "String -&gt;" half (String as source, coercing to a numeric, Boolean or Date
/// destination) is dispatched by those destination types instead, and lives in
/// <see cref="VBNumericLetCoercionTypeRuntimeSemantics"/>, <see cref="VBBooleanLetCoercionRuntimeSemantics"/>
/// and <see cref="VBDateLetCoercionRuntimeSemantics"/> respectively, for that same reason.
/// </remarks>
public record class VBStringLetCoercionRuntimeSemantics(
    IVerboseMessageBuilder FormatterService)
    : LetCoercionRuntimeSemantics<VBStringType>(FormatterService)
{
    public override LetCoercionResult EvaluateLetCoercion(
        ISymbolResolver resolver,
        VBOperatorExpression expression,
        LetCoercionStackFrame frame)
    {
        var cultureInfo = CultureInfo.InvariantCulture;
        return frame.SourceValue switch
        {
            VBStringValue stringSourceValue when frame.DestinationTypeDesc.Target is VBStringType
                // result is a copy of the source string.
                => LetCoercionResult.Success(new VBStringValue(stringSourceValue.Value)),

            VBNumericTypedValue numericSourceValue when frame.DestinationTypeDesc.Target is VBStringType
                => CoerceToVBString(numericSourceValue, cultureInfo),

            VBBooleanValue booleanSourceValue when frame.DestinationTypeDesc.Target is VBStringType
                => LetCoercionResult.Success(
                    new VBStringValue((bool)booleanSourceValue.Value
                        ? Tokens.True
                        : Tokens.False)),

            VBDateValue dateSourceValue when frame.DestinationTypeDesc.Target is VBStringType
                => LetCoercionResult.Success(new VBStringValue(FormatDate(dateSourceValue.Value))),

            // MS-VBAL 5.5.1.2.11: "The result is a 0-length string."
            VBEmptyValue when frame.DestinationTypeDesc.Target is VBStringType
                => LetCoercionResult.Success(VBStringValue.ZeroLengthString),

            VBArrayValue byteArraySource when byteArraySource.ItemType is VBByteType && frame.DestinationTypeDesc.Target is VBStringType
                => LetCoercionResult.Success(new VBStringValue(FromBytes(byteArraySource))),

            _ => LetCoercionResult.NotApplicable(frame)
        };
    }

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

    protected override ILetCoercionSemanticContextBuilder AnalyzeLetCoercionOperation(
        ILetCoercionSemanticContextBuilder builder,
        ISymbolResolver resolver,
        VBOperatorExpression expression,
        LetCoercionStackFrame frame)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// MS-VBAL 5.5.1.2.4: if the day value of the source date is 12/30/1899, only the date's time is
    /// converted (Long Time format) — compares <see cref="DateTime.Date"/> rather than requiring the
    /// exact zero serial value, since every time of day on 12/30/1899 is itself a "day value" of
    /// 12/30/1899. Otherwise the source date's full date and time value is converted (Short Date
    /// format); when the time-of-day is exactly midnight that full value has nothing to add, so only
    /// the date prints — real VBA drops a trailing "12:00:00 AM" from a date-only value.
    /// </summary>
    private static string FormatDate(DateTime value) => value.Date == VBDateType.Zero.Value.Date
        ? value.ToLongTimeString()
        : value.TimeOfDay == TimeSpan.Zero
            ? value.ToShortDateString()
            : $"{value.ToShortDateString()} {value.ToLongTimeString()}";

    private static LetCoercionResult CoerceToVBString(VBNumericTypedValue value, CultureInfo cultureInfo)
    {
        // BoxedValue's CLR type tracks the source's own numeric type (int for Long, short for
        // Integer, ...), not always double — a direct (double) unboxing cast throws for anything
        // that isn't already a boxed double. AsDouble widens through Convert.ToDouble instead.
        var numericValue = value.AsDouble;
        if (numericValue == 0)
        {
            return LetCoercionResult.Success(new VBStringValue(VBStringValue.Zero));
        }
        if (double.IsPositiveInfinity(numericValue))
        {
            return LetCoercionResult.Success(new VBStringValue(VBStringValue.PositiveInfinity));
        }
        if (double.IsNegativeInfinity(numericValue))
        {
            return LetCoercionResult.Success(new VBStringValue(VBStringValue.NegativeInfinity));
        }
        if (double.IsNaN(numericValue))
        {
            return LetCoercionResult.Success(new VBStringValue(VBStringValue.NaN));
        }

        var sign = numericValue < 0 ? "-" : string.Empty;
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

        var stringValue = integerPartDigitCount > significantIntegerDigits
            ? ToVBScientificNotation(numericValue, significantIntegerDigits, dot, cultureInfo)
            : ToVBNormalNotation(numericValue, significantIntegerDigits, dot);

        return LetCoercionResult.Success(new VBStringValue(stringValue));
    }

    /// <summary>
    /// MS-VBAL 5.5.1.2.4: "as many digits as possible of the fractional part of the number such that a
    /// maximum of [15, or 7 for Single] integer and fractional digits are printed total with trailing
    /// zeros removed" — <c>double</c>'s own default formatting prints its shortest round-trippable
    /// representation instead (up to 17 significant digits), so <c>CStr(0.1 + 0.2)</c> would otherwise
    /// read "0.30000000000000004" instead of "0.3".
    /// </summary>
    private static string ToVBNormalNotation(double value, int significantIntegerDigits, string decimalSeparator)
    {
        var sign = value < 0 ? "-" : string.Empty;
        var absoluteValue = Math.Abs(value);

        // decompose into a rounded significand + decimal exponent the same way ToVBScientificNotation
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

    private static string ToVBScientificNotation(double value, int significantIntegerDigits, string decimalSeparator, CultureInfo cultureInfo)
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
