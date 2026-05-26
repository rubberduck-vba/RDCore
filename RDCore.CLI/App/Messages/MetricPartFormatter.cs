using RDCore.CLI.App.Messages.Model;

namespace RDCore.CLI.App.Messages;

internal class MetricPartFormatter
{
    public static string FormatValue<T>(MetricKind kind, T value) 
        => kind switch
        {
            MetricKind.IntegerValue => $"{value}",
            MetricKind.NumericValue => $"{value:N1}",
            MetricKind.PercentageValue => $"{value:P1}",
            MetricKind.StopwatchMilliseconds => $"{value} ms",
            _ => value?.ToString() ?? string.Empty
        };
}
