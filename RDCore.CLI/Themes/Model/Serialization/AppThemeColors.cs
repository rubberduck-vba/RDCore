using System.Text.Json.Serialization;

namespace RDCore.CLI.Themes.Model.Serialization;

internal record class AppThemeColors
{
    [JsonPropertyName("fg-splash-logo")]
    public required string SplashLogoColor { get; init; }
    [JsonPropertyName("fg-splash-title")]
    public required string SplashTitleColor { get; init; }

    [JsonPropertyName("source-listings")]
    public required AppThemeSourceListingColors SyntaxHighlighting { get; init; }

    [JsonPropertyName("message-parts")]
    public required AppThemeMessagePartsColors Messages { get; init; }

}

internal record class AppThemeColorsModel
{
    internal AppThemeColorsModel(AppThemeColors colors, IThemeColorParser parser)
    {
        SplashLogoColor = parser.ParseThemeColor(colors.SplashLogoColor);
        SplashTitleColor = parser.ParseThemeColor(colors.SplashTitleColor);

        SyntaxHighlighting = new(colors.SyntaxHighlighting, parser);
        Messages = new(colors.Messages, parser);
    }
    
    public required int SplashLogoColor { get; init; }
    public required int SplashTitleColor { get; init; }

    public required AppThemeSourceListingColorsModel SyntaxHighlighting { get; init; }
    public required AppThemeMessagePartsColorsModel Messages { get; init; }
}