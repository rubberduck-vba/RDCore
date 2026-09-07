namespace RDCore.SDK.ConsoleIO.Model;

/// <summary>A timestamp shown ahead of the message, formatted <c>[yyyy-MM-dd HH:mm:ss.fff]</c>.</summary>
/// <param name="Timestamp">The instant to render.</param>
public record class ConsoleMessageTimestampPart(DateTimeOffset Timestamp)
    : ConsoleMessagePart(MessagePart.Timestamp, $"[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}]");

/// <summary>Creates <see cref="ConsoleMessageTimestampPart"/>s.</summary>
public static class ConsoleMessageTimestampPartFactory
{
    /// <summary>A timestamp part for the given instant.</summary>
    public static ConsoleMessagePart CreateTimestampPart(DateTimeOffset timestamp) => new ConsoleMessageTimestampPart(timestamp);
}
