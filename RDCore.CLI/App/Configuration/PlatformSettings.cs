using RDCore.SDK.Extensibility;
using RDCore.SDK.Platform;
using RDCore.SDK.Server.Configuration;
using System.Globalization;
using System.IO.Abstractions;

namespace RDCore.CLI.App.Configuration;

/// <summary>
/// The settings a user of the console can set, and where what they set is kept.
/// </summary>
internal interface IPlatformSettings
{
    /// <summary>
    /// Every setting of the platform: the platform's own, the console's, and those of each extension the platform has (<see cref="ExtensionInfo.Settings"/>),
    /// described in the language of the user interface.
    /// </summary>
    SettingsSchema Schema { get; }

    /// <summary>The settings the user set (<see cref="UserSettingsStore"/>).</summary>
    UserSettingsStore Store { get; }
}

/// <inheritdoc cref="IPlatformSettings"/>
/// <remarks>
/// The settings of an extension are read off its manifest, so that it need not run to be configured. An extension's setting whose key is one the platform or
/// another extension has already is not one of them: the first to declare a key is the one it is the setting of.
/// </remarks>
/// <param name="fileSystem">The file system the user's settings are in.</param>
/// <param name="environment">Where the user's folder is.</param>
/// <param name="extensions">The extensions the platform has.</param>
internal sealed class PlatformSettings(IFileSystem fileSystem, IPlatformEnvironment environment, IExtensionsProvider extensions) : IPlatformSettings
{
    private SettingsSchema? _schema;

    public SettingsSchema Schema => _schema ??= Read();

    public UserSettingsStore Store => new(fileSystem, UserSettingsStore.PathIn(environment), Schema);

    private SettingsSchema Read()
    {
        var settings = SettingsSchema.Platform.Combine(AppOptions.Schema).Settings.ToList();
        foreach (var extension in extensions.Discover())
        {
            settings.AddRange(extension.SettingsIn(CultureInfo.CurrentUICulture).Settings
                .Where(setting => !settings.Any(other => string.Equals(other.Key, setting.Key, StringComparison.OrdinalIgnoreCase))));
        }

        return SettingsSchema.From(settings);
    }
}
