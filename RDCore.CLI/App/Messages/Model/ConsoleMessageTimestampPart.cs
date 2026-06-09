namespace RDCore.CLI.App.Messages.Model;

internal record class ConsoleMessageTimestampPart(DateTimeOffset Timestamp) : ConsoleMessagePart(MessagePart.Timestamp, $"[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}]") { }
internal record class ConsoleMessageTimestampPartFactory
{
    public static ConsoleMessagePart CreateTimestampPart(DateTimeOffset timestamp) => new ConsoleMessageTimestampPart(timestamp);
}
