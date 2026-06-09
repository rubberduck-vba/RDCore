using System.Text.Json.Serialization;

namespace RDCore.CLI.Themes.Model.Serialization;

internal record class AppThemeMessagePartColors
{
    [JsonPropertyName("error")]
    public required string ErrorMessagePartColor { get; init; }
    [JsonPropertyName("success")]
    public required string SuccessMessagePartColor { get; init; }
    [JsonPropertyName("information")]
    public required string InformationMessagePartColor { get; init; }
    [JsonPropertyName("warning")]
    public required string WarningMessagePartColor { get; init; }
    [JsonPropertyName("trace")]
    public required string TraceMessagePartColor { get; init; }
}

internal record class AppThemeMessagePartColorsModel
{
    public AppThemeMessagePartColorsModel(AppThemeMessagePartColors config, IThemeColorParser parser)
    {
        ErrorMessagePartColor = parser.ParseThemeColor(config.ErrorMessagePartColor);
        SuccessMessagePartColor = parser.ParseThemeColor(config.SuccessMessagePartColor);
        InformationMessagePartColor = parser.ParseThemeColor(config.InformationMessagePartColor);
        WarningMessagePartColor = parser.ParseThemeColor(config.WarningMessagePartColor);
        TraceMessagePartColor = parser.ParseThemeColor(config.TraceMessagePartColor);
    }
    public int ErrorMessagePartColor { get; init; }
    public int SuccessMessagePartColor { get; init; }
    public int InformationMessagePartColor { get; init; }
    public int WarningMessagePartColor { get; init; }
    public int TraceMessagePartColor { get; init; }
}