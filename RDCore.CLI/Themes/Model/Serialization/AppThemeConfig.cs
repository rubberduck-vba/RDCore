using System.Text.Json.Serialization;

namespace RDCore.CLI.Themes.Model.Serialization;

public record class AppThemeConfig
{
    [JsonPropertyName("font-family")]
    public string FontFamily { get; init; } = "Consolas";

    [JsonPropertyName("shell")]
    public AppThemeShellConfig Shell { get; init; } = new();
}

public record class AppThemeConfigModel
{
    public AppThemeConfigModel(AppThemeConfig config, IThemeColorParser parser)
    {
        FontFamily = config.FontFamily;
        Shell = new(config.Shell, parser);
    }

    public string FontFamily { get; init; }
    public AppThemeShellConfigModel Shell { get; init; }
}