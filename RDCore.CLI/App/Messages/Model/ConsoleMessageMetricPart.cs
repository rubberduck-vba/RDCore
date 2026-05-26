namespace RDCore.CLI.App.Messages.Model;

internal enum MetricKind
{
    StringLiteral,
    IntegerValue,
    NumericValue,
    PercentageValue,
    StopwatchMilliseconds,
}

internal record class ConsoleMessageStringLiteralMetricPart(MetricKind Kind, string Placeholder, string StringValue)
    : ConsoleMessagePart(MessagePart.Metric, MetricPartFormatter.FormatValue(MetricKind.StringLiteral, StringValue)) { }
internal record class ConsoleMessageMetricPart(MetricKind Kind, string Placeholder, double NumericValue)
    : ConsoleMessageStringLiteralMetricPart(Kind, Placeholder, MetricPartFormatter.FormatValue(Kind, NumericValue)) { }

internal record class ConsoleMessageMetricPartFactory
{
    public static ConsoleMessagePart CreateMetricPart(MetricKind kind, string placeholder, double value) => new ConsoleMessageMetricPart(kind, placeholder, value);
    public static ConsoleMessagePart CreateMetricPart(string placeholder, string value) => new ConsoleMessageStringLiteralMetricPart(MetricKind.StringLiteral, placeholder, value);
}

