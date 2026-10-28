using RDCore.SDK.Platform.Protocol;
using System.Globalization;
using System.Resources;

namespace RDCore.SDK.Formatting;

/// <summary>
/// What the import of Rubberduck's indenter settings says to the user, in the language of the user interface.
/// </summary>
public static class IndenterSettingsImportText
{
    private static readonly ResourceManager Messages = new("RDCore.SDK.Formatting.IndenterImportMessages", typeof(IndenterSettingsImportText).Assembly);

    /// <summary>What a source of settings is called.</summary>
    /// <param name="source">The source.</param>
    /// <param name="culture">The language; the user interface's when <see langword="null"/>.</param>
    public static string SourceName(RubberduckIndenterSettingsSource source, CultureInfo? culture = null)
        => Say($"IndenterImport_Source_{source}", culture);

    /// <summary>The question whether to import the settings that were found.</summary>
    public static string Offer(RubberduckIndenterSettingsSource source, CultureInfo? culture = null) => Say("IndenterImport_Offer", culture, SourceName(source, culture));

    /// <summary>The choice that imports them.</summary>
    public static string Import(CultureInfo? culture = null) => Say("IndenterImport_Choice_Import", culture);

    /// <summary>The choice that does not, and does not ask again.</summary>
    public static string NotNow(CultureInfo? culture = null) => Say("IndenterImport_Choice_NotNow", culture);

    /// <summary>What an import did.</summary>
    public static string Imported(IndenterSettingsImportResult result, CultureInfo? culture = null)
    {
        var imported = Say("IndenterImport_Imported", culture, result.Imported, SourceName(result.Source, culture));
        return result.Refused.Count == 0 ? imported : $"{imported} {Say("IndenterImport_Refused", culture, string.Join(", ", result.Refused))}";
    }

    /// <summary>There are no settings to import.</summary>
    public static string Nothing(CultureInfo? culture = null) => Say("IndenterImport_Nothing", culture);

    /// <summary>The client was asked to take the settings, and took none.</summary>
    public static string Declined(IndenterSettingsImportResult result, CultureInfo? culture = null)
        => result.Refused.Count == 0 ? Say("IndenterImport_Declined", culture) : $"{Say("IndenterImport_Declined", culture)} {Say("IndenterImport_Refused", culture, string.Join(", ", result.Refused))}";

    /// <summary>The client does not read the settings of Rubberduck.</summary>
    public static string Unsupported(CultureInfo? culture = null) => Say("IndenterImport_Unsupported", culture);

    /// <summary>What was asked to import from is not something to.</summary>
    public static string UnknownSource(string name, CultureInfo? culture = null) => Say("IndenterImport_UnknownSource", culture, name);

    private static string Say(string key, CultureInfo? culture, params object[] arguments)
    {
        culture ??= CultureInfo.CurrentUICulture;
        return string.Format(culture, Messages.GetString(key, culture) ?? key, arguments);
    }
}
