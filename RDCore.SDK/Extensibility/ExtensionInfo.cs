using RDCore.SDK.Client;

namespace RDCore.SDK.Extensibility;

/// <summary>
/// The information contained in an <em>extension manifest</em>.
/// </summary>
/// <remarks>
/// 🧩 This is how RDCore recognizes an extension as such. This file should be marked as <strong>read-only</strong> at the OS level.
/// </remarks>
/// <param name="Name">The name of the extension executable (.exe).</param>
/// <param name="Title">The <em>friendly name</em> / title of the extension. This should also be the name of the folder.</param>
/// <param name="Version">The version of the extension.</param>
/// <param name="Publisher">The name of the publisher.</param>
/// <param name="PublisherWebUrl">The publisher's web URL.</param>
/// <param name="Description">A short description of the extension.</param>
/// <param name="Signature">The <em>binary signature</em> of the extension executable.</param>
/// <param name="Capabilities">The <em>advertised capabilities</em> of the extension.</param>
public record class ExtensionInfo(
    string Name,
    string Title,
    Version Version,
    string Publisher,
    string PublisherWebUrl,
    string Description,
    string Signature,
    PlatformExtensionServerCapability[] Capabilities)
{
    /// <summary>
    /// The settings the extension is configured with (<see cref="ExtensionSettingsAttribute"/>), without their descriptions, which are in
    /// <see cref="SettingDescriptions"/>; none for an extension that has none.
    /// </summary>
    public Server.Configuration.SettingDescriptor[] Settings { get; init; } = [];

    /// <summary>
    /// The description of each setting of <see cref="Settings"/>, by its key, in each language of the platform, by its two-letter name
    /// (<see cref="Server.Configuration.SettingsSchema.Languages"/>).
    /// </summary>
    public Dictionary<string, Dictionary<string, string>> SettingDescriptions { get; init; } = [];

    /// <summary>
    /// The manifest, with the settings the extension's assembly says it is configured with (<see cref="ExtensionSettingsAttribute"/>) and their descriptions in
    /// each language of the platform; a description the extension has none of in a language is the one its resources fall back on.
    /// </summary>
    /// <param name="assembly">The extension's assembly.</param>
    /// <returns>The manifest as it is, for an assembly that says nothing of its settings.</returns>
    public ExtensionInfo WithSettingsOf(System.Reflection.Assembly assembly)
    {
        if (System.Reflection.CustomAttributeExtensions.GetCustomAttribute<ExtensionSettingsAttribute>(assembly) is not { } declared)
        {
            return this;
        }

        var resources = new System.Resources.ResourceManager(declared.DescriptionsResource, assembly);
        var settings = Server.Configuration.SettingsSchema.For(declared.OptionsType, declared.Section).Settings.ToArray();
        return this with
        {
            Settings = settings,
            SettingDescriptions = Server.Configuration.SettingsSchema.Languages.ToDictionary(
                language => language,
                language => settings
                    .Select(setting => (setting.Key, Text: resources.GetString(setting.Key.Replace('.', '_'), System.Globalization.CultureInfo.GetCultureInfo(language))))
                    .Where(described => described.Text is not null)
                    .ToDictionary(described => described.Key, described => described.Text!)),
        };
    }

    /// <summary>
    /// The settings of the extension, described in a language: <paramref name="culture"/>'s, or the platform's first when it has no description in it.
    /// </summary>
    /// <param name="culture">The language of the user interface.</param>
    public Server.Configuration.SettingsSchema SettingsIn(System.Globalization.CultureInfo culture)
    {
        var descriptions = SettingDescriptions.GetValueOrDefault(culture.TwoLetterISOLanguageName)
            ?? SettingDescriptions.GetValueOrDefault(Server.Configuration.SettingsSchema.Languages[0])
            ?? [];

        return Server.Configuration.SettingsSchema.From(Settings.Select(setting => setting with { Description = descriptions.GetValueOrDefault(setting.Key) }));
    }
}

/// <summary>
/// Describes an extension capability.
/// </summary>
/// <param name="Name">The name of the capability</param>
/// <param name="IsSupported"><c>true</c> unless specified otherwise.</param>
public record class PlatformExtensionServerCapability(string Name, bool IsSupported = true);

