namespace RDCore.CLI.ConsoleIO.Model;

public record class ConsoleMessageStackTracePart(Exception Exception)
    : ConsoleMessagePart(MessagePart.StackTrace, Exception.StackTrace ?? string.Empty);

public static class ConsoleMessageStackTracePartFactory
{
    public static ConsoleMessagePart CreateStackTracePart(Exception exception) => new ConsoleMessageStackTracePart(exception);
}
