namespace RDCore.SDK.ConsoleIO.Model;

/// <summary>How a placeholder value is formatted before it is substituted into the body.</summary>
public enum PlaceholderKind
{
    /// <summary>A literal string, substituted as-is.</summary>
    StringLiteral,
    /// <summary>A whole number.</summary>
    IntegerValue,
    /// <summary>A number, one decimal place.</summary>
    NumericValue,
    /// <summary>A ratio rendered as a percentage.</summary>
    PercentageValue,
    /// <summary>A millisecond duration, suffixed <c>ms</c>.</summary>
    StopwatchMilliseconds,
}

/// <summary>Formats a placeholder value according to its <see cref="PlaceholderKind"/>.</summary>
public static class PlaceholderPartFormatter
{
    /// <summary>Formats <paramref name="value"/> for substitution.</summary>
    public static string FormatValue<T>(PlaceholderKind kind, T value)
        => kind switch
        {
            PlaceholderKind.StringLiteral => (string)(object)value!,
            PlaceholderKind.IntegerValue => $"{value}",
            PlaceholderKind.NumericValue => $"{value:N1}",
            PlaceholderKind.PercentageValue => $"{value:P1}",
            PlaceholderKind.StopwatchMilliseconds => $"{value} ms",
            _ => value?.ToString() ?? string.Empty,
        };
}
