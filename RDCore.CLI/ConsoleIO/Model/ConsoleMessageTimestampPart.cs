namespace RDCore.CLI.ConsoleIO.Model;

public record class ConsoleMessageTimestampPart(DateTimeOffset Timestamp)
    : ConsoleMessagePart(MessagePart.Timestamp, $"[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}]");

public static class ConsoleMessageTimestampPartFactory
{
    public static ConsoleMessagePart CreateTimestampPart(DateTimeOffset timestamp) => new ConsoleMessageTimestampPart(timestamp);
}
