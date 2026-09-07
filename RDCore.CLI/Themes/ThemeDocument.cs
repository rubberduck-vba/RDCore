using System.Text.Json.Serialization;

namespace RDCore.CLI.Themes;

/// <summary>
/// The deserialized form of a <c>.theme</c> file. Every colour value is either a <see cref="Palette"/>
/// key or an inline Spectre style token (a named colour, <c>#rrggbb</c>, or <c>"fg on bg"</c>).
/// </summary>
public sealed record class ThemeDocument
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "unnamed";

    [JsonPropertyName("author")]
    public string Author { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; init; } = "0";

    /// <summary>Named 24-bit colours the other blocks reference.</summary>
    [JsonPropertyName("palette")]
    public Dictionary<string, string> Palette { get; init; } = [];

    [JsonPropertyName("shell")]
    public ThemeShell Shell { get; init; } = new();

    /// <summary>Per-<c>MessageKind</c> icon + per-<c>MessagePart</c> style, keyed by the lowercase kind name.</summary>
    [JsonPropertyName("messages")]
    public Dictionary<string, ThemeMessageStyle> Messages { get; init; } = [];

    [JsonPropertyName("syntax")]
    public ThemeSyntax Syntax { get; init; } = new();

    [JsonPropertyName("splash")]
    public ThemeSplash Splash { get; init; } = new();
}

/// <summary>Shell frame colours.</summary>
public sealed record class ThemeShell
{
    [JsonPropertyName("background")]
    public string Background { get; init; } = "black";

    [JsonPropertyName("foreground")]
    public string Foreground { get; init; } = "grey";
}

/// <summary>The icon and per-part styles for one message kind.</summary>
public sealed record class ThemeMessageStyle
{
    [JsonPropertyName("icon")]
    public string Icon { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; init; } = "default";

    [JsonPropertyName("body")]
    public string Body { get; init; } = "default";

    [JsonPropertyName("verbose")]
    public string Verbose { get; init; } = "grey";

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; init; } = "grey";

    [JsonPropertyName("metric")]
    public string Metric { get; init; } = "default";

    [JsonPropertyName("stack-trace")]
    public string StackTrace { get; init; } = "grey";
}

/// <summary>Syntax-highlight roles for program-mode source listings (the <c>LIST</c> command).</summary>
public sealed record class ThemeSyntax
{
    [JsonPropertyName("keyword")]
    public string Keyword { get; init; } = "default";

    [JsonPropertyName("comment")]
    public string Comment { get; init; } = "green";

    [JsonPropertyName("string")]
    public string String { get; init; } = "default";

    [JsonPropertyName("number")]
    public string Number { get; init; } = "default";

    [JsonPropertyName("identifier")]
    public string Identifier { get; init; } = "default";

    [JsonPropertyName("identifier-class")]
    public string IdentifierClass { get; init; } = "default";

    [JsonPropertyName("identifier-const")]
    public string IdentifierConst { get; init; } = "default";
}

/// <summary>Splash-screen colours.</summary>
public sealed record class ThemeSplash
{
    [JsonPropertyName("logo")]
    public string Logo { get; init; } = "default";

    [JsonPropertyName("title")]
    public string Title { get; init; } = "default";
}
