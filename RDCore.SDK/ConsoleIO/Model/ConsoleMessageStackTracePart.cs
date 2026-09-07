namespace RDCore.SDK.ConsoleIO.Model;

/// <summary>An exception stack trace.</summary>
/// <param name="Exception">The exception whose <see cref="System.Exception.StackTrace"/> is rendered.</param>
public record class ConsoleMessageStackTracePart(Exception Exception)
    : ConsoleMessagePart(MessagePart.StackTrace, Exception.StackTrace ?? string.Empty);

/// <summary>Creates <see cref="ConsoleMessageStackTracePart"/>s.</summary>
public static class ConsoleMessageStackTracePartFactory
{
    /// <summary>A stack-trace part for the given exception.</summary>
    public static ConsoleMessagePart CreateStackTracePart(Exception exception) => new ConsoleMessageStackTracePart(exception);
}
