namespace RDCore.CLI;

/// <summary>
/// CLI-specific settings, bound from the <c>Configuration:CLI</c> section of <c>appsettings.json</c>.
/// </summary>
public class AppOptions
{
    /// <summary>Whether the console renderer applies a theme (vs. the neutral built-in style set).</summary>
    public bool ThemesEnabled { get; init; } = true;

    /// <summary>Directory scanned for <c>*.theme</c> files, in addition to the built-in default.</summary>
    public string ThemesDiscoveryPath { get; init; } = "./Themes";

    /// <summary>The name of the theme to select once themes are loaded.</summary>
    public string Theme { get; init; } = "rdc-default";
}
