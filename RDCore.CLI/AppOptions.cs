using RDCore.SDK.Server.Configuration;

namespace RDCore.CLI;

/// <summary>
/// CLI-specific settings, bound from the <c>Configuration:CLI</c> section of <c>appsettings.json</c>.
/// </summary>
/// <remarks>
/// The console reads them when it starts: a setting a user changes is the next console's (<see cref="SettingReach.NextStart"/>).
/// </remarks>
public class AppOptions
{
    /// <summary>The key of the section the settings are bound from, under <see cref="SettingsSchema.Root"/>.</summary>
    public const string Section = "CLI";

    /// <summary>The settings of the console, described in the language of the user interface.</summary>
    public static SettingsSchema Schema => SettingsSchema.For<AppOptions>(Section, Resources.ResourceManager);

    /// <summary>Whether the console renderer applies a theme (vs. the neutral built-in style set).</summary>
    [Setting(SettingReach.NextStart)]
    public bool ThemesEnabled { get; init; } = true;

    /// <summary>Directory scanned for <c>*.theme</c> files, in addition to the built-in default.</summary>
    [Setting(SettingReach.NextStart)]
    public string ThemesDiscoveryPath { get; init; } = "./Themes";

    /// <summary>The name of the theme to select once themes are loaded.</summary>
    [Setting(SettingReach.NextStart)]
    public string Theme { get; init; } = "dark";
}
