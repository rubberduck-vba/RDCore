using RDCore.CLI.App.Messages.Model;

namespace RDCore.CLI.App.Messages;

public class PlaceholderPartFormatter
{
    public static string FormatValue<T>(PlaceholderKind kind, T value) 
        => kind switch
        {
            PlaceholderKind.StringLiteral => (string)(object)value!,
            PlaceholderKind.IntegerValue => $"{value}",
            PlaceholderKind.NumericValue => $"{value:N1}",
            PlaceholderKind.PercentageValue => $"{value:P1}",
            PlaceholderKind.StopwatchMilliseconds => $"{value} ms",
            _ => value?.ToString() ?? string.Empty
        };
}
