namespace RDCore.SDK.ConsoleIO.Model;

/// <summary>The message text.</summary>
/// <param name="Body">The body text (may contain <c>{$NAME}</c> placeholder spans).</param>
public record class ConsoleMessageBodyPart(string Body) : ConsoleMessagePart(MessagePart.Body, Body);

/// <summary>Creates <see cref="ConsoleMessageBodyPart"/>s.</summary>
public static class ConsoleMessageBodyPartFactory
{
    /// <summary>A body part from literal text.</summary>
    public static ConsoleMessagePart CreateMessageBodyPart(string body) => new ConsoleMessageBodyPart(body);

    /// <summary>A body part from an exception's message.</summary>
    public static ConsoleMessagePart CreateMessageBodyPart(Exception exception) => new ConsoleMessageBodyPart(exception.Message);
}
