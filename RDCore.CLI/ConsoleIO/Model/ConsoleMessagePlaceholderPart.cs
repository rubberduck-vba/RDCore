namespace RDCore.CLI.ConsoleIO.Model;

public record class ConsoleMessageStringLiteralPlaceholderPart(PlaceholderKind Kind, string Placeholder, string StringValue)
    : ConsoleMessagePart(MessagePart.Metric, PlaceholderPartFormatter.FormatValue(PlaceholderKind.StringLiteral, StringValue));

public record class ConsoleMessagePlaceholderPart(PlaceholderKind Kind, string Placeholder, double NumericValue)
    : ConsoleMessageStringLiteralPlaceholderPart(Kind, Placeholder, PlaceholderPartFormatter.FormatValue(Kind, NumericValue));

public record class ConsoleMessageColoredPlaceholderPart(string Placeholder, string StringValue, ConsoleRgbColor Color)
    : ConsoleMessageStringLiteralPlaceholderPart(PlaceholderKind.StringLiteral, Placeholder, StringValue);

public static class ConsoleMessageMetricPartFactory
{
    public static ConsoleMessagePart CreatePlaceholderPart(string placeholder, string value, ConsoleRgbColor color)
        => new ConsoleMessageColoredPlaceholderPart(placeholder, value, color);

    public static ConsoleMessagePart CreatePlaceholderPart(PlaceholderKind kind, string placeholder, double value)
        => new ConsoleMessagePlaceholderPart(kind, placeholder, value);

    public static ConsoleMessagePart CreatePlaceholderPart(string placeholder, string value)
        => new ConsoleMessageStringLiteralPlaceholderPart(PlaceholderKind.StringLiteral, placeholder, value);
}
