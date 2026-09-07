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

/// <summary>Creates placeholder / metric parts.</summary>
public static class ConsoleMessageMetricPartFactory
{
    /// <summary>A metric part for a numeric value.</summary>
    public static ConsoleMessagePart CreatePlaceholderPart(PlaceholderKind kind, string placeholder, double value)
        => new ConsoleMessagePlaceholderPart(kind, placeholder, value);

    /// <summary>A metric part for a literal string value.</summary>
    public static ConsoleMessagePart CreatePlaceholderPart(string placeholder, string value)
        => new ConsoleMessageStringLiteralPlaceholderPart(PlaceholderKind.StringLiteral, placeholder, value);
}
