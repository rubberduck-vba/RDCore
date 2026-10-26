namespace RDCore.SDK.ConsoleIO.Model;

/// <summary>
/// A named span whose formatted value is substituted into the body where <see cref="Placeholder"/>
/// (a <c>{$NAME}</c> token) appears, and styled as <see cref="MessagePart.Metric"/>.
/// </summary>
/// <param name="Kind">How the value is formatted.</param>
/// <param name="Placeholder">The literal token to replace in the body, e.g. <c>{$COUNT}</c>.</param>
/// <param name="StringValue">The already-formatted replacement text.</param>
public record class ConsoleMessageStringLiteralPlaceholderPart(PlaceholderKind Kind, string Placeholder, string StringValue)
    : ConsoleMessagePart(MessagePart.Metric, PlaceholderPartFormatter.FormatValue(PlaceholderKind.StringLiteral, StringValue));

/// <summary>A <see cref="ConsoleMessageStringLiteralPlaceholderPart"/> whose value is numeric.</summary>
/// <param name="Kind">How the value is formatted.</param>
/// <param name="Placeholder">The literal token to replace in the body.</param>
/// <param name="NumericValue">The numeric value, formatted per <paramref name="Kind"/>.</param>
public record class ConsoleMessagePlaceholderPart(PlaceholderKind Kind, string Placeholder, double NumericValue)
    : ConsoleMessageStringLiteralPlaceholderPart(Kind, Placeholder, PlaceholderPartFormatter.FormatValue(Kind, NumericValue));

/// <summary>
/// A <see cref="ConsoleMessageStringLiteralPlaceholderPart"/> written in a colour of its own rather than styled as <see cref="MessagePart.Metric"/>: a
/// glyph whose colour is part of what it means, whatever the theme - the red heart of the slogan.
/// </summary>
/// <param name="Placeholder">The literal token to replace in the body, e.g. <c>{$HEART}</c>.</param>
/// <param name="StringValue">The replacement text.</param>
/// <param name="Color">The colour the replacement text is written in.</param>
public record class ConsoleMessageColoredPlaceholderPart(string Placeholder, string StringValue, ConsoleRgbColor Color)
    : ConsoleMessageStringLiteralPlaceholderPart(PlaceholderKind.StringLiteral, Placeholder, StringValue);

/// <summary>Creates placeholder / metric parts.</summary>
public static class ConsoleMessageMetricPartFactory
{
    /// <summary>A part for a literal string value written in <paramref name="color"/> (<see cref="ConsoleMessageColoredPlaceholderPart"/>).</summary>
    public static ConsoleMessagePart CreatePlaceholderPart(string placeholder, string value, ConsoleRgbColor color)
        => new ConsoleMessageColoredPlaceholderPart(placeholder, value, color);

    /// <summary>A metric part for a numeric value.</summary>
    public static ConsoleMessagePart CreatePlaceholderPart(PlaceholderKind kind, string placeholder, double value)
        => new ConsoleMessagePlaceholderPart(kind, placeholder, value);

    /// <summary>A metric part for a literal string value.</summary>
    public static ConsoleMessagePart CreatePlaceholderPart(string placeholder, string value)
        => new ConsoleMessageStringLiteralPlaceholderPart(PlaceholderKind.StringLiteral, placeholder, value);
}
