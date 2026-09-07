namespace RDCore.SDK.ConsoleIO.Model;

/// <summary>A short title or diagnostic code shown ahead of the body.</summary>
/// <param name="Title">The title text.</param>
public record class ConsoleMessageTitlePart(string Title) : ConsoleMessagePart(MessagePart.Title, Title);

/// <summary>Creates <see cref="ConsoleMessageTitlePart"/>s.</summary>
public static class ConsoleMessageTitlePartFactory
{
    /// <summary>A title part from literal text.</summary>
    public static ConsoleMessagePart CreateTitlePart(string title) => new ConsoleMessageTitlePart(title);
}
