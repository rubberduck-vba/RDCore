using RDCore.Runtime.Semantics.Conversion;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.Runtime.StdLib.Formatting;

/// <summary>Why a value could not be formatted: an error, and the rule it broke.</summary>
internal sealed record class FormatFailure(VBRuntimeErrorId Error, string Rule);

/// <summary>
/// <strong>MS-VBAL §6.1.2.11.1.8 Format</strong>'s format expressions: a named format, or a user-defined one of up to four sections.
/// </summary>
/// <remarks>
/// <para>
/// What a value is formatted as is what the section that writes it formats. A number, a <c>Date</c>, a <c>Boolean</c> and
/// <c>Empty</c> are numbers to a number section and dates to a date section, each Let-coerced as it would be to a <c>Double</c> or a
/// <c>Date</c>; a <c>String</c> is a number if it reads as one, else a date if it reads as one, and otherwise it is written as it is,
/// unless the first section is a string section, which writes any value as its <c>String</c>.
/// </para>
/// <para>
/// The section a number is written by is chosen by its sign before it is rounded: the second for a negative number and the third
/// for zero, when the format has them and they are not empty; otherwise the first, with a minus sign before what it writes for a
/// negative number. So <c>Format(-0.001, "0.00")</c> is "-0.00", and <c>Format(-0.001, "0.00;(0.00)")</c> is "(0.00)".
/// </para>
/// </remarks>
internal static class FormatExpression
{
    /// <summary>"Format truncates format to 257 characters."</summary>
    public const int LongestFormat = 257;

    // the serial numbers of January 1, 100 and of the day after December 31, 9999 (MS-VBAL 2.1).
    private const double FirstSerial = -657434d;
    private const double SerialAfterLast = 2958466d;

    private enum Named
    {
        None,
        GeneralNumber,
        Currency,
        YesNo,
        TrueFalse,
        OnOff,
        // a named format written as the user-defined one it is.
        NumberTokens,
        DateTokens,
    }

    /// <summary>
    /// Writes <paramref name="value"/> by <paramref name="format"/>. A value no section writes (a <c>Null</c> without a section for
    /// it) is not written, and <paramref name="isNull"/> says so.
    /// </summary>
    /// <param name="output">Where the value is written.</param>
    /// <param name="value">The value, unwrapped: Null, Empty, a Boolean, a number, a Date or a String.</param>
    /// <param name="format">The format, which is not a zero-length string.</param>
    /// <param name="settings">What the format is written in.</param>
    /// <param name="isNull">Whether the result is Null.</param>
    /// <returns>The failure, or <see langword="null"/>.</returns>
    public static FormatFailure? Write(ref TextBuilder output, VBTypedValue value, ReadOnlySpan<char> format, in FormatSettings settings, out bool isNull)
    {
        isNull = false;
        format = format[..Math.Min(format.Length, LongestFormat)];
        var (named, tokens) = NamedFormat(format);
        if (named is not Named.None)
        {
            if (value is VBNullValue)
            {
                isNull = true;
                return null;
            }

            return WriteNamed(ref output, value, named, tokens, settings);
        }

        Span<FormatSection> sections = stackalloc FormatSection[FormatSections.Most];
        sections = sections[..FormatSections.Split(format, sections)];
        var isText = sections[0].Kind is FormatSectionKind.Text;
        if (value is VBNullValue)
        {
            // a string format's second section is for Null and a zero-length string, a number format's fourth for Null.
            var nullSection = isText && sections.Length >= 2 ? 1 : !isText && sections.Length == FormatSections.Most ? 3 : -1;
            if (nullSection < 0)
            {
                isNull = true;
                return null;
            }

            return WriteSection(ref output, sections[nullSection], format, VBEmptyValue.Empty, NumberValue.Zero, signed: false, settings);
        }

        if (isText)
        {
            var text = TextOf(value, settings);
            var section = text.Length == 0 && sections.Length >= 2 ? sections[1] : sections[0];
            TextFormatting.WriteSection(ref output, section.Of(format), text, settings.Culture.TextInfo);
            return null;
        }

        if (!TryReadNumber(value, settings, out var number))
        {
            output.Append(((VBStringValue)value).Value);
            return null;
        }

        var sign = number.Sign;
        var chosen = sign < 0 && sections.Length >= 2 && !sections[1].IsEmpty ? 1
            : sign == 0 && sections.Length >= 3 && !sections[2].IsEmpty ? 2
            : 0;
        return WriteSection(ref output, sections[chosen], format, value, number, signed: sign < 0 && chosen == 0, settings);
    }

    /// <summary>
    /// Whether <paramref name="format"/> writes a <c>Null</c>: a user-defined string format with a second section, or number format
    /// with a fourth. Any other format makes the result of a <c>Null</c> <c>Null</c>.
    /// </summary>
    public static bool WritesNull(ReadOnlySpan<char> format)
    {
        format = format[..Math.Min(format.Length, LongestFormat)];
        if (format.IsEmpty || NamedFormat(format).Named is not Named.None)
        {
            return false;
        }

        Span<FormatSection> sections = stackalloc FormatSection[FormatSections.Most];
        var count = FormatSections.Split(format, sections);
        return sections[0].Kind is FormatSectionKind.Text ? count >= 2 : count == FormatSections.Most;
    }

    /// <summary>
    /// Writes <paramref name="value"/> by a date format written as user-defined date symbols, as <c>FormatDateTime</c> and the named date
    /// formats do.
    /// </summary>
    public static FormatFailure? WriteDate(ref TextBuilder output, VBTypedValue value, ReadOnlySpan<char> tokens, in FormatSettings settings)
    {
        var (moment, failure) = TryMomentOf(value, settings);
        if (failure is not null)
        {
            return failure;
        }

        DateFormatting.WriteSection(ref output, tokens, moment, settings);
        return null;
    }

    /// <summary>The number a value is, as Format reads it: <see langword="false"/> for a <c>String</c> that is neither a number nor a date.</summary>
    public static bool TryReadNumber(VBTypedValue value, in FormatSettings settings, out NumberValue number)
    {
        switch (value)
        {
            case VBStringValue text:
                if (ValueConversions.ToNumeric(text, VBDoubleType.TypeInfo, settings.Regional).Value is VBDoubleValue read)
                {
                    number = NumberValue.Floating(read.Value, VBDoubleType.SignificantIntegerDigits);
                    return true;
                }

                if (ValueConversions.TryInterpretDate(text.Value, out var date, settings.Regional))
                {
                    number = NumberValue.Floating(date.SerialValue, VBDoubleType.SignificantIntegerDigits);
                    return true;
                }

                number = default;
                return false;
            default:
                number = NumberOf(value);
                return true;
        }
    }

    /// <summary>
    /// The number a value of a type that is one is: a whole number, a Currency or a Decimal exactly, a Single to 7 significant digits,
    /// a Double and a Date's serial number to 15; True is -1, and False and Empty are 0.
    /// </summary>
    public static NumberValue NumberOf(VBTypedValue value) => value switch
    {
        VBSingleValue single => NumberValue.Floating(single.Value, VBSingleType.SignificantIntegerDigits),
        VBDoubleValue @double => NumberValue.Floating(@double.Value, VBDoubleType.SignificantIntegerDigits),
        VBDateValue date => NumberValue.Floating(date.SerialValue, VBDoubleType.SignificantIntegerDigits),
        VBBooleanValue boolean => NumberValue.Whole((bool)boolean.Value ? -1 : 0),
        _ when ValueConversions.WholeValueOf(value) is { } whole => NumberValue.Whole(whole),
        _ when ValueConversions.FixedValueOf(value) is { } @fixed => NumberValue.Fixed(@fixed),
        _ => NumberValue.Zero,
    };

    /// <summary>
    /// The date a value is Let-coerced to (<strong>MS-VBAL §5.5.1.2.3</strong>), to the nearest second: one outside the range of dates is
    /// error 6, as the coercion's overflow, and one before the first day of the session's calendar is error 5.
    /// </summary>
    public static (DateTime Moment, FormatFailure? Failure) TryMomentOf(VBTypedValue value, in FormatSettings settings)
    {
        var converted = ValueConversions.ToDate(value, settings.Regional);
        if (converted.Value is not VBDateValue date)
        {
            return (default, new FormatFailure(converted.Error ?? VBRuntimeErrorId.TypeMismatch, "the value is not a date"));
        }

        // a Date that is no day, which only its bytes can make, is error 5 (ValueConversions.TryMomentOf).
        if (!ValueConversions.TryMomentOf(date, out _))
        {
            return (default, new FormatFailure(VBRuntimeErrorId.InvalidProcedureCallOrArgument, "the Date is no day from January 1, 100 to December 31, 9999"));
        }

        var serial = date.SerialValue;
        if (!(serial >= FirstSerial && serial < SerialAfterLast))
        {
            return (default, new FormatFailure(VBRuntimeErrorId.Overflow, "the date is from January 1, 100 to December 31, 9999"));
        }

        var exact = DateTime.FromOADate(serial);
        var rounded = (exact.Ticks + TimeSpan.TicksPerSecond / 2) / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond;
        var moment = new DateTime(Math.Min(rounded, DateTime.MaxValue.Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond));
        var calendar = settings.Calendar;
        return moment < calendar.MinSupportedDateTime || moment > calendar.MaxSupportedDateTime
            ? (default, new FormatFailure(VBRuntimeErrorId.InvalidProcedureCallOrArgument, "the date is one of the current calendar"))
            : (moment, null);
    }

    private static FormatFailure? WriteSection(ref TextBuilder output, FormatSection section, ReadOnlySpan<char> format, VBTypedValue value, in NumberValue number, bool signed, in FormatSettings settings)
    {
        var text = section.Of(format);
        switch (section.Kind)
        {
            case FormatSectionKind.Date:
                return WriteDate(ref output, value, text, settings);
            case FormatSectionKind.Text:
                TextFormatting.WriteSection(ref output, text, TextOf(value, settings), settings.Culture.TextInfo);
                return null;
            default:
                if (!number.IsFinite)
                {
                    output.Append(TextOf(value, settings));
                    return null;
                }

                NumberFormatting.WriteSection(ref output, text, number, signed, settings);
                return null;
        }
    }

    private static FormatFailure? WriteNamed(ref TextBuilder output, VBTypedValue value, Named named, string tokens, in FormatSettings settings)
    {
        if (named is Named.DateTokens)
        {
            // a String that is no date, and no number, is written as it is.
            if (value is VBStringValue text && ValueConversions.ToDate(text, settings.Regional).Value is null)
            {
                output.Append(text.Value);
                return null;
            }

            return WriteDate(ref output, value, tokens, settings);
        }

        if (!TryReadNumber(value, settings, out var number))
        {
            output.Append(((VBStringValue)value).Value);
            return null;
        }

        switch (named)
        {
            case Named.GeneralNumber:
                // "Display number with no thousand separator": the number as a Let-coercion to String writes it.
                output.Append(TextOf(number.ToTypedValue(), settings));
                break;
            case Named.Currency:
                NumberFormatting.WriteStyled(ref output, number, NumberStyle.Currency, -1, null, null, null, settings);
                break;
            case Named.YesNo:
                output.Append(number.Sign == 0 ? "No" : "Yes");
                break;
            case Named.TrueFalse:
                output.Append(number.Sign == 0 ? "False" : "True");
                break;
            case Named.OnOff:
                output.Append(number.Sign == 0 ? "Off" : "On");
                break;
            default:
                if (!number.IsFinite)
                {
                    output.Append(TextOf(value, settings));
                    break;
                }

                NumberFormatting.WriteSection(ref output, tokens, number, signed: number.Sign < 0, settings);
                break;
        }

        return null;
    }

    // the named formats, read in any case, and the user-defined formats the ones that are user-defined formats are written as.
    private static (Named Named, string Tokens) NamedFormat(ReadOnlySpan<char> format)
    {
        foreach (var (name, named, tokens) in NamedFormats)
        {
            if (format.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return (named, tokens);
            }
        }

        return (Named.None, string.Empty);
    }

    private static readonly (string Name, Named Named, string Tokens)[] NamedFormats =
    [
        ("General Number", Named.GeneralNumber, string.Empty),
        ("Currency", Named.Currency, string.Empty),
        // "at least one digit to the left and two digits to the right of the decimal separator".
        ("Fixed", Named.NumberTokens, "0.00"),
        // "with thousand separator, at least one digit to the left and two digits to the right".
        ("Standard", Named.NumberTokens, "#,##0.00"),
        // "multiplied by 100 with a percent sign (%) appended to the right; always display two digits to the right".
        ("Percent", Named.NumberTokens, "0.00%"),
        ("Scientific", Named.NumberTokens, "0.00E+00"),
        ("Yes/No", Named.YesNo, string.Empty),
        ("True/False", Named.TrueFalse, string.Empty),
        ("On/Off", Named.OnOff, string.Empty),
        // "a date and/or time [...] If there is no fractional part, display only a date [...] If there is no integer part, display time only".
        ("General Date", Named.DateTokens, "c"),
        ("Long Date", Named.DateTokens, "dddddd"),
        // 🎯 "the medium date format appropriate for the language version of the host application", which is English: 27-Jan-93.
        ("Medium Date", Named.DateTokens, "dd-mmm-yy"),
        ("Short Date", Named.DateTokens, "ddddd"),
        ("Long Time", Named.DateTokens, "ttttt"),
        // "12-hour format using hours and minutes and the AM/PM designator".
        ("Medium Time", Named.DateTokens, "hh:nn AMPM"),
        // "the 24-hour format, for example, 17:45".
        ("Short Time", Named.DateTokens, "hh:nn"),
    ];

    // a value as its String, the way a Let-coercion to String writes it in the environment's regional settings.
    private static string TextOf(VBTypedValue value, in FormatSettings settings)
        => (ValueConversions.ToText(value, settings.Culture).Value as VBStringValue)?.Value ?? string.Empty;
}
