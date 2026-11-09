using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;

namespace RDCore.Runtime.Semantics.Conversion;

public static partial class ValueConversions
{
    /// <summary>
    /// The day and time <paramref name="date"/> is, when it is one: a day from January 1, 100 to December 31, 9999 (<strong>MS-VBAL §2.1</strong>).
    /// </summary>
    /// <remarks>
    /// Only a <c>Date</c>'s bytes can hold a serial that is no day's: an <c>LSet</c> of a record over another, a <c>Get</c>, a write to its address. Such a
    /// <c>Date</c> is a <c>Date</c> still, and reads as the <c>Double</c> it holds; what reads it as a day (Let-coercion to <c>String</c>, <c>Format</c>,
    /// <c>Year</c> and the other date functions) raises error 5, <em>Invalid procedure call or argument</em> (MS-VBA, recorded in Excel).
    /// </remarks>
    /// <param name="date">The <c>Date</c>.</param>
    /// <param name="moment">The day and time, when <paramref name="date"/> is one.</param>
    /// <returns>Whether <paramref name="date"/> is a day.</returns>
    internal static bool TryMomentOf(VBDateValue date, out DateTime moment)
    {
        var serial = date.SerialValue;
        moment = VBDateType.IsValidSerial(serial) ? DateTime.FromOADate(serial) : default;
        return VBDateType.IsValidSerial(serial);
    }

    /// <summary>
    /// Converts <paramref name="source"/> to a <c>Date</c> (<strong>MS-VBAL §5.5.1.2.3</strong>), from a
    /// <c>Date</c>, any numeric type, <c>Boolean</c>, <c>String</c> (<strong>§5.5.1.2.4</strong>) or
    /// <c>Empty</c> (<strong>§5.5.1.2.11</strong>).
    /// </summary>
    /// <param name="source">The value, unwrapped from any <c>Variant</c> that held it.</param>
    /// <param name="regional">The regional settings a <c>String</c> is read by; en-US when none are given.</param>
    public static ValueConversionResult ToDate(VBTypedValue source, RegionalSettings? regional = null) => source switch
    {
        // result is a copy of the source date.
        VBDateValue date => ValueConversionResult.Success(new VBDateValue(date.SerialValue)),

        // result is the source value converted to Double, then the Double interpreted as a standard serial value.
        VBNumericTypedValue or VBBooleanValue => DoubleToDate(ToNumeric(source, VBDoubleType.TypeInfo)),

        VBStringValue text => StringToDate(text.Value, regional),

        // MS-VBAL 5.5.1.2.11: "The result is 12/30/1899 00:00:00."
        VBEmptyValue => ValueConversionResult.Success(new VBDateValue(VBDateType.Zero.SerialValue)),

        VBNullValue => ValueConversionResult.Failure(VBRuntimeErrorId.InvalidUseOfNull),

        // MS-VBAL 5.5.1.2.9: an Error converts to nothing but a Variant or an Error.
        VBErrorValue => ValueConversionResult.Failure(VBRuntimeErrorId.TypeMismatch),

        _ => ValueConversionResult.NotApplicable,
    };

    // a number that is no date's serial is an overflow (MS-VBA, recorded in Excel).
    private static ValueConversionResult DoubleToDate(ValueConversionResult number) => number.Value switch
    {
        VBDoubleValue converted when VBDateType.IsValidSerial(converted.Value) => ValueConversionResult.Success(new VBDateValue(converted.Value)),
        VBDoubleValue => ValueConversionResult.Failure(VBRuntimeErrorId.Overflow),
        _ => number,
    };

    /// <summary>
    /// Interprets <paramref name="text"/> as a date, a time, or both - the first of the two ways a <c>String</c> Let-coerces to a
    /// <c>Date</c> (<strong>MS-VBAL §5.5.1.2.4</strong>), the other being to read it as a number.
    /// </summary>
    /// <remarks>
    /// <c>IsDate</c> asks this alone: a string that is a date only by being a number is not one to it.
    /// <para>
    /// The string is read by the grammar of a date literal, <c>date-or-time</c> (<strong>MS-VBAL §3.3.3</strong>), without its <c>#</c> delimiters - which is
    /// what MS-VBA does (recorded in Excel): a month and a day are told apart by which of them can be a month, a comma is a date separator, a dot is a
    /// time separator (<c>1.5</c> is 1:05), and the month names are English. The year a date without one is in is the current year.
    /// </para>
    /// </remarks>
    /// <param name="text">The string.</param>
    /// <param name="date">The date it reads as, when it reads as one.</param>
    /// <param name="regional">The regional settings - the window of two-digit years - it is read by; en-US when none are given.</param>
    /// <returns><see langword="false"/> when the string is not a date or a time.</returns>
    public static bool TryInterpretDate(string? text, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out VBDateValue? date, RegionalSettings? regional = null)
    {
        date = text is not null
            && DateGrammar.TryRead(text, DateTime.Today.Year, out var value, (regional ?? RegionalSettings.Default).TwoDigitYearMax)
                ? new VBDateValue(value.ToOADate())
                : null;
        return date is not null;
    }

    // MS-VBAL 5.5.1.2.4: try date/time/time/date interpretation first; otherwise, if the string can be
    // interpreted as a number or currency value within Double's magnitude range, convert that Double
    // to Date. A Double-conversion overflow is reported as Type mismatch (13), not Overflow (6).
    private static ValueConversionResult StringToDate(string? text, RegionalSettings? regional)
    {
        if (TryInterpretDate(text, out var date, regional))
        {
            return ValueConversionResult.Success(date);
        }

        if (StringToNumeric(text, VBDoubleType.TypeInfo, regional).Value is not VBDoubleValue number)
        {
            // unparseable, or the string-to-Double step overflowed: reported as Type mismatch (13) either way.
            return ValueConversionResult.Failure(VBRuntimeErrorId.TypeMismatch);
        }

        return number.Value >= VBDateType.MinSerial && number.Value <= VBDateType.MaxSerial
            ? ValueConversionResult.Success(new VBDateValue(number.Value))
            : ValueConversionResult.Failure(VBRuntimeErrorId.TypeMismatch);
    }
}
