using System.Text.Json.Serialization;

namespace RDCore.CLI.Themes.Model.Serialization;

internal record class AppThemeSourceListingColors
{
    [JsonPropertyName("keyword")]
    public required string KeywordColor { get; init; }
    [JsonPropertyName("comment")]
    public required string CommentColor { get; init; }
    [JsonPropertyName("string-literal")]
    public required string StringLiteralColor { get; init; }
    [JsonPropertyName("number-literal")]
    public required string NumberLiteralColor { get; init; }
    [JsonPropertyName("identifier")]
    public required string IdentifierColor { get; init; }
    [JsonPropertyName("identifier-class")]
    public required string ClassIdentifierColor { get; init; }
    [JsonPropertyName("identifier-const")]
    public required string ConstIdentifierColor { get; init; }
}

internal record class AppThemeSourceListingColorsModel
{
    internal AppThemeSourceListingColorsModel(AppThemeSourceListingColors listings, IThemeColorParser parser)
    {
        KeywordColor = parser.ParseThemeColor(listings.KeywordColor);
        CommentColor = parser.ParseThemeColor(listings.CommentColor);
        StringLiteralColor = parser.ParseThemeColor(listings.StringLiteralColor);
        NumberLiteralColor = parser.ParseThemeColor(listings.NumberLiteralColor);
        IdentifierColor = parser.ParseThemeColor(listings.IdentifierColor);
        ClassIdentifierColor = parser.ParseThemeColor(listings.ClassIdentifierColor);
        ConstIdentifierColor = parser.ParseThemeColor(listings.ConstIdentifierColor);
    }

    public int KeywordColor { get; init; }
    public int CommentColor { get; init; }
    public int StringLiteralColor { get; init; }
    public int NumberLiteralColor { get; init; }
    public int IdentifierColor { get; init; }
    public int ClassIdentifierColor { get; init; }
    public int ConstIdentifierColor { get; init; }
}