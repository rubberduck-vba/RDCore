namespace RDCore.CLI.App.Messages.Model;

internal enum MetricKind
{
    IntegerValue,
    NumericValue,
    PercentageValue,
    StopwatchMilliseconds,
}

internal record class ConsoleMessageMetricPart(MetricKind Kind, string Placeholder, double NumericValue) : ConsoleMessagePart(MessagePart.Metric, MetricPartFormatter.FormatValue(Kind, NumericValue)) { }
internal record class ConsoleMessageMetricPartFactory
{
    public static ConsoleMessagePart CreateMetricPart(MetricKind kind, string placeholder, double value) => new ConsoleMessageMetricPart(kind, placeholder, value);
}

