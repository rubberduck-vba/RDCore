using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Reflection;
using System.Resources;

namespace RDCore.SDK.Server;

/// <summary>
/// The language of the platform's user interface, as its configuration says it (<see cref="Configuration.SdkRegionalOptions.Language"/>): the language every
/// message of a component is in, and the one a client tells the servers it starts (<see cref="ClientLocale"/>).
/// </summary>
/// <remarks>
/// <para>
/// Only the language of the user interface is set (<see cref="CultureInfo.CurrentUICulture"/>, and the process's default). How numbers and dates are written
/// and read stays with the other regional settings (<see cref="Runtime.RegionalSettings"/>), and <see cref="CultureInfo.CurrentCulture"/> is left as it is.
/// </para>
/// <para>
/// A component applies the setting when it starts (<see cref="AppHost{TApp}"/>), before it writes anything, and says once what came of it
/// (<see cref="UserInterfaceLanguageSetting.Message"/>). A server keeps the language it set over the <c>locale</c> its client says
/// (<see cref="UserInterfaceLanguageSetting.Overrides"/>), so that every component of the platform speaks it whatever its client shows.
/// </para>
/// </remarks>
public static class UserInterfaceLanguage
{
    /// <summary>
    /// The key of the setting in a component's configuration.
    /// </summary>
    public const string ConfigurationKey = "Configuration:Environment:Regional:Language";

    // the platform's own messages: the neutral resources, and a satellite for each language they are translated in.
    private static readonly ResourceManager Translations = TraceMessages.ResourceManager;

    /// <summary>
    /// The language the platform's own messages are written in, which every language they are not translated in falls back to: <c>en</c>.
    /// </summary>
    public static CultureInfo NeutralLanguage { get; } = CultureInfo.GetCultureInfo(
        typeof(UserInterfaceLanguage).Assembly.GetCustomAttribute<NeutralResourcesLanguageAttribute>()?.CultureName is { Length: > 0 } name ? name : "en");

    /// <summary>
    /// What the setting comes to, without applying it. The message is in the current language of the user interface.
    /// </summary>
    /// <param name="setting">The value of the setting: a culture name, or nothing for the machine's language.</param>
    public static UserInterfaceLanguageSetting Resolve(string? setting)
    {
        var current = CultureInfo.CurrentUICulture;
        if (string.IsNullOrWhiteSpace(setting))
        {
            return new(UserInterfaceLanguageOutcome.Machine, null, current, LogLevel.None, null);
        }

        var asked = setting.Trim();
        if (ClientLocale.CultureOf(asked) is not { } culture)
        {
            return new(UserInterfaceLanguageOutcome.Unknown, asked, current, LogLevel.Warning, MessageOf(nameof(TraceMessages.UserInterfaceLanguage_Unknown), current, asked, current.Name));
        }

        // the message is in the language the user reads now: the one asked for is one the platform has no translation in.
        return IsTranslated(culture)
            ? new(UserInterfaceLanguageOutcome.Applied, asked, culture, LogLevel.Information, $"🌐 The user interface is in '{culture.Name}' (Environment.Regional.Language).")
            : new(UserInterfaceLanguageOutcome.NotTranslated, asked, culture, LogLevel.Information,
                MessageOf(nameof(TraceMessages.UserInterfaceLanguage_NotTranslated), current, culture.Name, NeutralLanguage.Name));
    }

    /// <summary>
    /// Applies the setting: a culture the platform knows becomes the language of the user interface, of the current thread and of every thread that has not
    /// set its own.
    /// </summary>
    /// <param name="setting">The value of the setting: a culture name, or nothing for the machine's language.</param>
    /// <returns>What the setting came to. The language is left as it is when the setting is empty, or names no culture the platform knows.</returns>
    public static UserInterfaceLanguageSetting Apply(string? setting)
    {
        var resolved = Resolve(setting);
        if (resolved.Overrides)
        {
            _ = ClientLocale.Apply(resolved.Culture.Name);
        }

        return resolved;
    }

    /// <summary>
    /// Applies the setting a component's configuration has (<see cref="ConfigurationKey"/>).
    /// </summary>
    /// <param name="configuration">The component's configuration.</param>
    /// <returns>What the setting came to.</returns>
    public static UserInterfaceLanguageSetting Apply(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return Apply(configuration[ConfigurationKey]);
    }

    /// <summary>
    /// Whether the platform's messages are translated in the language of a culture: in the culture itself or in one of its parents (<c>fr</c> for
    /// <c>fr-CA</c>). The neutral language (<see cref="NeutralLanguage"/>) is translated.
    /// </summary>
    /// <param name="culture">The culture.</param>
    public static bool IsTranslated(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        for (var language = culture; language.Name.Length > 0; language = language.Parent)
        {
            if (string.Equals(language.Name, NeutralLanguage.Name, StringComparison.OrdinalIgnoreCase) || HasSatellite(language))
            {
                return true;
            }
        }

        return false;
    }

    // a satellite of the platform's messages for the culture itself, and not one of its parents'.
    private static bool HasSatellite(CultureInfo culture)
    {
        try
        {
            return Translations.GetResourceSet(culture, createIfNotExists: true, tryParents: false) is not null;
        }
        catch (Exception exception) when (exception is MissingManifestResourceException or MissingSatelliteAssemblyException or IOException)
        {
            return false;
        }
    }

    private static string MessageOf(string name, CultureInfo culture, string first, string second)
        => string.Format(culture, Translations.GetString(name, culture) ?? name, first, second);
}

/// <summary>
/// What the setting of the language of the user interface comes to (<see cref="UserInterfaceLanguage.Resolve"/>).
/// </summary>
public enum UserInterfaceLanguageOutcome
{
    /// <summary>
    /// The setting is empty: the user interface is in the machine's language.
    /// </summary>
    Machine,

    /// <summary>
    /// The setting names a culture whose language the platform is translated in: the user interface is in it.
    /// </summary>
    Applied,

    /// <summary>
    /// The setting names a culture whose language the platform is not translated in: the culture is applied, and its messages fall back to English.
    /// </summary>
    NotTranslated,

    /// <summary>
    /// The setting names no culture the platform knows: the user interface stays in the machine's language.
    /// </summary>
    Unknown,
}

/// <summary>
/// What the setting of the language of the user interface comes to, and what a component says of it when it starts.
/// </summary>
/// <param name="Outcome">What the setting comes to.</param>
/// <param name="Setting">The culture name the setting says, trimmed; <see langword="null"/> when it is empty.</param>
/// <param name="Culture">The language of the user interface it gives: the culture it names, or the machine's language when it names none the platform knows.</param>
/// <param name="Level">The level <see cref="Message"/> is logged at: <see cref="LogLevel.Warning"/> for a name that is no culture, otherwise
/// <see cref="LogLevel.Information"/>; <see cref="LogLevel.None"/> when there is nothing to say.</param>
/// <param name="Message">What a component logs once when it starts, in the language the user interface was in before the setting was applied;
/// <see langword="null"/> when the setting is empty.</param>
public sealed record class UserInterfaceLanguageSetting(UserInterfaceLanguageOutcome Outcome, string? Setting, CultureInfo Culture, LogLevel Level, string? Message)
{
    /// <summary>
    /// Whether the setting decides the language of the user interface: over the machine's, and over the <c>locale</c> a client says.
    /// </summary>
    public bool Overrides => Outcome is UserInterfaceLanguageOutcome.Applied or UserInterfaceLanguageOutcome.NotTranslated;
}
