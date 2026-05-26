using System.Text.Json.Serialization;

namespace RDCore.CLI.Themes.Model.Serialization;

internal record class AppThemeMessagePartsColors
{
    [JsonPropertyName("timestamp")]
    public required AppThemeMessagePartColors Timestamp { get; init; }
    [JsonPropertyName("title")]
    public required AppThemeMessagePartColors Title { get; init; }
    [JsonPropertyName("body")]
    public required AppThemeMessagePartColors MessageBody { get; init; }
    [JsonPropertyName("verbose")]
    public required AppThemeMessagePartColors Verbose { get; init; }
    [JsonPropertyName("stack-trace")]
    public required AppThemeMessagePartColors StackTrace { get; init; }
    [JsonPropertyName("metric")]
    public required AppThemeMessagePartColors ValuePlaceholder { get; init; }
}

internal record class AppThemeMessagePartsColorsModel
{
    public AppThemeMessagePartsColorsModel(AppThemeMessagePartsColors config, IThemeColorParser parser)
    {
        Timestamp = new(config.Timestamp, parser);
        Title = new(config.Title, parser);
        MessageBody = new(config.MessageBody, parser);
        Verbose = new(config.Verbose, parser);
        StackTrace = new(config.StackTrace, parser);
        ValuePlaceholder = new(config.ValuePlaceholder, parser);

    }
    public AppThemeMessagePartColorsModel Timestamp { get; init; }
    public AppThemeMessagePartColorsModel Title { get; init; }
    public AppThemeMessagePartColorsModel MessageBody { get; init; }
    public AppThemeMessagePartColorsModel Verbose { get; init; }
    public AppThemeMessagePartColorsModel StackTrace { get; init; }
    public AppThemeMessagePartColorsModel ValuePlaceholder { get; init; }
}

internal record class AppThemeMessagePartsColorStubs
{
    public AppThemeMessagePartsColorStubs(AppThemeMessagePartsColors config, IThemeColorParser parser)
    {
        Timestamp = new(config.Timestamp, parser);
        Title = new(config.Title, parser);
        MessageBody = new(config.MessageBody, parser);
        Verbose = new(config.Verbose, parser);
        StackTrace = new(config.StackTrace, parser);
        ValuePlaceholder = new(config.ValuePlaceholder, parser);
    }

    public AppThemeMessagePartColorsModel Timestamp { get; init; }
    public AppThemeMessagePartColorsModel Title { get; init; }
    public AppThemeMessagePartColorsModel MessageBody { get; init; }
    public AppThemeMessagePartColorsModel Verbose { get; init; }
    public AppThemeMessagePartColorsModel StackTrace { get; init; }
    public AppThemeMessagePartColorsModel ValuePlaceholder { get; init; }
}