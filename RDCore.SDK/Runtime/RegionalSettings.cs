using System.Globalization;

namespace RDCore.SDK.Runtime;

/// <summary>
/// The regional settings a number or a date is written in as text, and a <c>String</c> is read by when it is Let-coerced to a number or a date:
/// <strong>MS-VBAL §5.5.1.2.4</strong>'s "active host-defined regional settings", and the window two-digit years are in.
/// </summary>
/// <remarks>
/// <para>
/// These are the platform's, set in its configuration (<see cref="Server.Configuration.SdkEnvironmentOptions.Regional"/>), and not the machine's or the
/// culture's: a program writes and reads a number the same way wherever it runs, unless the platform is told otherwise. The default is en-US, which is what
/// MS-VBA writes and reads on an en-US machine (recorded in Excel): <c>1,000</c> is a thousand, <c>$1</c> is one, <c>30</c> is the year 2030, and
/// <c>CStr(2.5)</c> is <c>2.5</c>.
/// </para>
/// <para>
/// MS-VBA has one set of regional settings, the system's, which <c>CStr</c> writes in and <c>CDbl</c> and <c>CDate</c> read by: what one writes, the
/// other reads back. So these are both ways too, and the culture of the environment (its LCID) says nothing of how a number or a date is written.
/// 🚧 A date is read by MS-VBAL §3.3.3's rules, which take an ambiguous one month first, whatever <see cref="ShortDatePattern"/> says.
/// </para>
/// </remarks>
/// <param name="DecimalSymbol">What separates the whole part of a number from its fraction.</param>
/// <param name="DigitGroupingSymbol">What groups the digits of a number; ignored where it is read, after the first digit.</param>
/// <param name="CurrencySymbol">What a number may be written with before or after it, as an amount of money.</param>
/// <param name="TwoDigitYearMax">The last year a year written with one or two digits can be: the hundred years ending with it are the ones such a year is in.</param>
public sealed record class RegionalSettings(string DecimalSymbol, string DigitGroupingSymbol, string CurrencySymbol, int TwoDigitYearMax)
{
    /// <summary>
    /// The settings of en-US: <c>.</c>, <c>,</c>, <c>$</c>, two-digit years up to 2049, and dates written <c>M/d/yyyy h:mm:ss tt</c>, as Windows has them
    /// today.
    /// </summary>
    public static RegionalSettings Default { get; } = new(".", ",", "$", 2049);

    /// <summary>
    /// The Short Date format a date is written in, as a .NET custom date format: <c>M/d/yyyy</c> by default.
    /// </summary>
    public string ShortDatePattern { get; init; } = "M/d/yyyy";

    /// <summary>
    /// The Long Time format a time is written in, as a .NET custom time format: <c>h:mm:ss tt</c> by default.
    /// </summary>
    public string LongTimePattern { get; init; } = "h:mm:ss tt";

    /// <summary>
    /// What a time before noon is written with: <c>AM</c> by default.
    /// </summary>
    public string AMDesignator { get; init; } = "AM";

    /// <summary>
    /// What a time from noon on is written with: <c>PM</c> by default.
    /// </summary>
    public string PMDesignator { get; init; } = "PM";

    /// <summary>
    /// The formats a date is written in: <see cref="ShortDatePattern"/> and <see cref="LongTimePattern"/>, in the Gregorian calendar, with <c>/</c> and
    /// <c>:</c> the separators they are written with.
    /// </summary>
    public DateTimeFormatInfo DateFormat => DateFormats.GetValue(this, settings => settings.MakeDateFormat());

    // made once for each instance, outside the record's own fields - which its equality is made of.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<RegionalSettings, DateTimeFormatInfo> DateFormats = [];

    private DateTimeFormatInfo MakeDateFormat()
    {
        var format = (DateTimeFormatInfo)CultureInfo.InvariantCulture.DateTimeFormat.Clone();
        format.ShortDatePattern = ShortDatePattern;
        format.LongTimePattern = LongTimePattern;
        format.AMDesignator = AMDesignator;
        format.PMDesignator = PMDesignator;
        format.DateSeparator = "/";
        format.TimeSeparator = ":";
        return DateTimeFormatInfo.ReadOnly(format);
    }
}
