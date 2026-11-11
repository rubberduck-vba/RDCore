using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Globalization;

namespace RDCore.Runtime.StdLib.Formatting;

/// <summary>
/// The currency symbol <strong>MS-VBAL §6.1.2.11.1.10 FormatCurrency</strong> writes an amount with, "the implementation-defined currency
/// symbol": MS-VBA's, and the same character on every system.
/// </summary>
/// <remarks>
/// <para>
/// MS-VBA reads the symbol from Windows' own locale data (NLS). .NET reads it from ICU, and ICU's data is not the same on every system: Windows' and
/// Linux's ICU write a yen as <c>￥</c> (U+FFE5) where NLS and macOS's ICU write <c>¥</c> (U+00A5).
/// </para>
/// <para>
/// The table holds MS-VBA's symbol for each currency, by its ISO 4217 code, where NLS and ICU were measured to differ (<c>GetLocaleInfoEx</c>'s
/// <c>LOCALE_SCURRENCY</c> against .NET's <see cref="NumberFormatInfo.CurrencySymbol"/>, on Windows 11). It covers a currency only where NLS gives it
/// one symbol in every culture whose currency it is, and only where a culture an LCID can name differs. Every other culture writes the symbol of
/// its culture data.
/// </para>
/// </remarks>
internal static class CurrencySymbols
{
    private static readonly FrozenDictionary<string, string> MSVBA = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["JPY"] = "\u00A5",
        ["CNY"] = "\u00A5",
        ["TWD"] = "NT$",
        ["RUB"] = "\u20BD",
        ["NOK"] = "kr",
        ["SEK"] = "kr",
        ["RON"] = "lei",
        ["IDR"] = "Rp",
        ["NGN"] = "\u20A6",
        ["PYG"] = "\u20B2",
        ["PKR"] = "Rs",
        ["NPR"] = "\u0930\u0941",
        ["TMT"] = "m.",
        ["JOD"] = "\u062F.\u0627.\u200F",
        ["DOP"] = "$",
        ["CLP"] = "$",
        ["CAD"] = "$",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly ConcurrentDictionary<string, string> Made = new(StringComparer.Ordinal);

    /// <summary>
    /// The symbol <paramref name="culture"/> writes an amount of money with: MS-VBA's for its currency where the table has one, and its culture
    /// data's otherwise.
    /// </summary>
    /// <param name="culture">The culture the amount is formatted in.</param>
    /// <returns>The symbol, the same character on every system for a currency the table has.</returns>
    public static string Of(CultureInfo culture)
        => Made.GetOrAdd(culture.Name, static (_, culture) => Make(culture), culture);

    private static string Make(CultureInfo culture)
    {
        var own = culture.NumberFormat.CurrencySymbol;
        if (culture.IsNeutralCulture || culture.Name.Length == 0)
        {
            return own;
        }

        try
        {
            return MSVBA.TryGetValue(new RegionInfo(culture.Name).ISOCurrencySymbol, out var symbol) ? symbol : own;
        }
        catch (ArgumentException)
        {
            // a culture of no region has no currency of its own.
            return own;
        }
    }
}
