using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using System.Globalization;

namespace RDCore.Runtime.Semantics.Conversion;

public static partial class ValueConversions
{
    /// <summary>
    /// Converts <paramref name="source"/> to a <c>Date</c> (<strong>MS-VBAL §5.5.1.2.3</strong>), from a
    /// <c>Date</c>, any numeric type, <c>Boolean</c>, <c>String</c> (<strong>§5.5.1.2.4</strong>) or
    /// <c>Empty</c> (<strong>§5.5.1.2.11</strong>).
    /// </summary>
    /// <param name="source">The value, unwrapped from any <c>Variant</c> that held it.</param>
    public static ValueConversionResult ToDate(VBTypedValue source) => source switch
    {
        // result is a copy of the source date.
        VBDateValue date => ValueConversionResult.Success(new VBDateValue(date.SerialValue)),

        // result is the source value converted to Double, then the Double interpreted as a standard serial value.
        VBNumericTypedValue or VBBooleanValue => DoubleToDate(ToNumeric(source, VBDoubleType.TypeInfo)),

        VBStringValue text => StringToDate(text.Value),

        // MS-VBAL 5.5.1.2.11: "The result is 12/30/1899 00:00:00."
        VBEmptyValue => ValueConversionResult.Success(new VBDateValue(VBDateType.Zero.SerialValue)),

        VBNullValue => ValueConversionResult.Failure(VBRuntimeErrorId.InvalidUseOfNull),

        // MS-VBAL 5.5.1.2.9: an Error converts to nothing but a Variant or an Error.
        VBErrorValue => ValueConversionResult.Failure(VBRuntimeErrorId.TypeMismatch),

        _ => ValueConversionResult.NotApplicable,
    };

    private static ValueConversionResult DoubleToDate(ValueConversionResult number)
        => number.Value is VBDoubleValue converted
            ? ValueConversionResult.Success(new VBDateValue(converted.Value))
            : number;

    /// <summary>
    /// Interprets <paramref name="text"/> as a date, a time, or both - the first of the two ways a <c>String</c> Let-coerces to a
    /// <c>Date</c> (<strong>MS-VBAL §5.5.1.2.4</strong>), the other being to read it as a number.
    /// </summary>
    /// <remarks>
    /// <c>IsDate</c> asks this alone: a string that is a date only by being a number is not one to it.
    /// </remarks>
    /// <param name="text">The string.</param>
    /// <param name="date">The date it reads as, when it reads as one.</param>
    /// <returns><see langword="false"/> when the string is not a date or a time.</returns>
    public static bool TryInterpretDate(string? text, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out VBDateValue? date)
    {
        // 🚧 TODO MS-VBA reads a date string by the host's regional settings and its own grammar (day/month order swapped when the
        // month would be out of range, a month name anywhere, two-part dates without a year, and more): .NET's invariant parse is an
        // approximation of it, which disagrees on a few strings.
        date = DateTime.TryParse(text, CultureInfo.InvariantCulture, out var dateValue) ? new VBDateValue(dateValue.ToOADate()) : null;
        return date is not null;
    }

    // MS-VBAL 5.5.1.2.4: try date/time/time/date interpretation first; otherwise, if the string can be
    // interpreted as a number or currency value within Double's magnitude range, convert that Double
    // to Date. A Double-conversion overflow is reported as Type mismatch (13), not Overflow (6).
    private static ValueConversionResult StringToDate(string? text)
    {
        if (TryInterpretDate(text, out var date))
        {
            return ValueConversionResult.Success(date);
        }

        if (StringToNumeric(text, VBDoubleType.TypeInfo).Value is not VBDoubleValue number)
        {
            // unparseable, or the string-to-Double step overflowed: reported as Type mismatch (13) either way.
            return ValueConversionResult.Failure(VBRuntimeErrorId.TypeMismatch);
        }

        return number.Value >= VBDateType.MinSerial && number.Value <= VBDateType.MaxSerial
            ? ValueConversionResult.Success(new VBDateValue(number.Value))
            : ValueConversionResult.Failure(VBRuntimeErrorId.TypeMismatch);
    }
}
