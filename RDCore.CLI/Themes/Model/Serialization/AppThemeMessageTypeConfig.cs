using System.Text.Json.Serialization;

namespace RDCore.CLI.Themes.Model.Serialization;

public record class AppThemeMessageTypeConfig
{
    [JsonPropertyName("icon")]
    public string Icon { get; init; } = string.Empty;

    [JsonPropertyName("theme-light")]
    public string ThemeLight { get; init; } = ConsoleColor.Gray.ToString();
    [JsonPropertyName("theme-accent1")]
    public string ThemeAccent1 { get; init; } = ConsoleColor.DarkCyan.ToString();
    [JsonPropertyName("theme-accent2")]
    public string ThemeAccent2 { get; init; } = ConsoleColor.White.ToString();
    [JsonPropertyName("theme-dark")]
    public string ThemeDark { get; init; } = ConsoleColor.DarkGray.ToString();
}

public record class AppThemeMessageTypeConfigModel
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