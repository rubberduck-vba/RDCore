using System.Text.Json.Serialization;

namespace RDCore.CLI;

public class AppOptions
{
    [JsonPropertyName("themes-enabled")]
    public bool ThemesEnabled { get; init; } = false;
    [JsonPropertyName("themes-path")]
    public string ThemesDiscoveryPath { get; init; } = "./themes";
    [JsonPropertyName("theme")]
    public string Theme { get; init; } = "rdc-default";
}
