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
internal static class ClientLocale
{
    /// <summary>
    /// The language tag a client says the language of its user interface by (<c>fr-CA</c>); <see langword="null"/> for the invariant culture, which has none.
    /// </summary>
    /// <param name="culture">The language of the client's user interface: its <see cref="CultureInfo.CurrentUICulture"/>.</param>
    public static string? TagOf(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return culture.Name.Length > 0 ? culture.Name : null;
    }

    /// <summary>
    /// The language a client says it shows, in an <c>initialize</c> request: the request's own <c>locale</c>, which a client of the protocol says, and
    /// otherwise what a platform's own client says in its <c>initializationOptions</c> (<see cref="Platform.Protocol.RDCoreInitializationOptions.Locale"/>).
    /// </summary>
    /// <remarks>
    /// The first of the two that is a language the platform knows is the one (<see cref="Apply"/>): a request's <c>locale</c> that is no known culture does
    /// not hide a known one in the <c>initializationOptions</c>. When neither is known, the first one said is, and is not applied.
    /// </remarks>
    /// <param name="locale">The request's <c>locale</c>.</param>
    /// <param name="initializationOptions">What the client says in its <c>initializationOptions</c>, if it is a platform's.</param>
    public static string? Of(string? locale, Platform.Protocol.RDCoreInitializationOptions? initializationOptions)
    {
        string?[] said = [locale, initializationOptions?.Locale];
        return said.FirstOrDefault(tag => CultureOf(tag) is not null) ?? said.FirstOrDefault(tag => !string.IsNullOrWhiteSpace(tag));
    }

    // the request's own locale: the one the receiver kept from the message (InitializeLocale). OmniSharp 0.19.9 gives its handlers an InitializeParams whose
    // Locale is always null, so the request is read first only for a version of OmniSharp that keeps it.
    internal static string? Said(OmniSharp.Extensions.LanguageServer.Protocol.Server.ILanguageServer server, OmniSharp.Extensions.LanguageServer.Protocol.Models.InitializeParams request)
        => !string.IsNullOrWhiteSpace(request.Locale)
            ? request.Locale
            : (server.Services.GetService(typeof(Services.InitializeLocale)) as Services.InitializeLocale)?.Locale;

    // the culture of a tag, when it is one the platform knows: ICU makes a culture of any well-formed tag, and only a known one is a language it could speak.
    private static CultureInfo? CultureOf(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        try
        {
            return CultureInfo.GetCultureInfo(tag.Trim(), predefinedOnly: true);
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// Makes the language a client says it shows the language of the user interface.
    /// </summary>
    /// <param name="locale">The client's <c>locale</c>: a language tag such as <c>fr-CA</c>, <c>en</c> or <c>de-ch</c>.</param>
    /// <returns>The culture now in use; <see langword="null"/> when the client says none, or one the platform does not know, which leaves the one in use.</returns>
    public static CultureInfo? Apply(string? locale)
    {
        if (CultureOf(locale) is not { } culture)
        {
            return null;
        }

        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
        return culture;
    }
}
