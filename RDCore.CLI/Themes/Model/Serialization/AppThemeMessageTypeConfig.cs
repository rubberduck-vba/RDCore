using System.Text.Json.Serialization;

namespace RDCore.CLI.Themes.Model.Serialization;

internal record class AppThemeMessageTypeConfig
{
    [JsonPropertyName("icon")]
    public string Icon { get; init; } = string.Empty;

    [JsonPropertyName("theme-light")]
    public string ThemeLight { get; init; } = "b9b9c3";
    [JsonPropertyName("theme-accent1")]
    public string ThemeAccent1 { get; init; } = "9898a4";
    [JsonPropertyName("theme-accent2")]
    public string ThemeAccent2 { get; init; } = "ffffff";
    [JsonPropertyName("theme-dark")]
    public string ThemeDark { get; init; } = "62626f";
}

internal record class AppThemeMessageTypeConfigModel
{
    public AppThemeMessageTypeConfigModel(AppThemeMessageTypeConfig config, IThemeColorParser parser)
    {
        Icon = config.Icon;

        ThemeLight = parser.ParseThemeColor(config.ThemeLight);
        ThemeAccent1 = parser.ParseThemeColor(config.ThemeAccent1);
        ThemeAccent2 = parser.ParseThemeColor(config.ThemeAccent2);
        ThemeDark = parser.ParseThemeColor(config.ThemeDark);
    }

    public string Icon { get; init; }

    public int ThemeLight { get; init; }
    public int ThemeAccent1 { get; init; } 
    public int ThemeAccent2 { get; init; } 
    public int ThemeDark { get; init; }
}