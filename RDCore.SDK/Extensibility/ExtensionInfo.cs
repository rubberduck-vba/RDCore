using RDCore.SDK.Client;

namespace RDCore.SDK.Extensibility;

/// <summary>
/// The information contained in an <em>extension manifest</em>.
/// </summary>
/// <remarks>
/// 🧩 This is how RDCore recognizes an extension as such. This file should be marked as <strong>read-only</strong> at the OS level.
/// </remarks>
public record class ExtensionInfo
{
    private readonly string? _id;

    /// <summary>
    /// The name of the extension executable (.exe).
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The <em>friendly name</em> / title of the extension. This should also be the name of the folder.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// The stable identifier of the extension; its <see cref="Title"/> when the manifest declares none.
    /// </summary>
    public string Id
    {
        get => _id ?? Title;
        init => _id = value;
    }

    /// <summary>
    /// The version of the extension.
    /// </summary>
    public required Version Version { get; init; }

    /// <summary>
    /// The name of the publisher.
    /// </summary>
    public required string Publisher { get; init; }

    /// <summary>
    /// The publisher's web URL.
    /// </summary>
    public required string PublisherWebUrl { get; init; }

    /// <summary>
    /// A short description of the extension.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// The <em>file hash</em> of the extension executable: its base64-encoded SHA-512 hash.
    /// </summary>
    public required string FileHash { get; init; }

    /// <summary>
    /// The <em>advertised capabilities</em> of the extension.
    /// </summary>
    public required PlatformExtensionServerCapability[] Capabilities { get; init; }

    /// <summary>
    /// The three-letter prefix of the diagnostic codes the extension's publisher issues; <c>null</c> for none.
    /// </summary>
    public string? DiagnosticPrefix { get; init; }

    /// <summary>
    /// The version of <c>RDCore.SDK</c> the extension was built against, as its assembly references it; <c>null</c> when the manifest does not say.
    /// </summary>
    public Version? SdkVersion { get; init; }

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

