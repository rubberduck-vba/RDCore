namespace RDCore.CLI.App.Messages.Model;

internal record class ConsoleMessageStackTracePart(Exception Exception) : ConsoleMessagePart(MessagePart.StackTrace, Exception.StackTrace ?? string.Empty) { }
internal record class ConsoleMessageStackTracePartFactory
{
    public static ConsoleMessagePart CreateStackTracePart(Exception exception) => new ConsoleMessageStackTracePart(exception);
}
