using System.Globalization;

namespace RDCore.SDK.Server;

/// <summary>
/// The language of the user interface a client says it shows (<strong>LSP 3.17</strong> <c>InitializeParams.locale</c>), as the language a server
/// speaks in: its diagnostics, its refusals, the titles of its actions, its hovers.
/// </summary>
/// <remarks>
/// <para>
/// Only the language of the user interface is the client's (<see cref="CultureInfo.CurrentUICulture"/>). How numbers and dates are written and read
/// is not: the semantics of the language do not depend on the culture of whoever reads them.
/// </para>
/// <para>
/// The language is the process's default as well as the current one, so that work the server begins later, on other threads - a child process it starts
/// and initializes, which it tells the same <c>locale</c> - speaks it too.
/// </para>
/// </remarks>
public static class ClientLocale
{
    /// <summary>
    /// Makes the language a client says it shows the language of the user interface.
    /// </summary>
    /// <param name="locale">The client's <c>locale</c>: a language tag such as <c>fr-CA</c>, <c>en</c> or <c>de-ch</c>.</param>
    /// <returns>The culture now in use; <see langword="null"/> when the client says none, or one the platform does not know, which leaves the one in use.</returns>
    public static CultureInfo? Apply(string? locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
        {
            return null;
        }

        CultureInfo culture;
        try
        {
            // ICU makes a culture of any well-formed tag: only one it knows is a language the platform could speak.
            culture = CultureInfo.GetCultureInfo(locale.Trim(), predefinedOnly: true);
        }
        catch (CultureNotFoundException)
        {
            return null;
        }

        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
        return culture;
    }
}
