namespace RDCore.CLI.ConsoleIO.Model;

public record class ConsoleMessageTitlePart(string Title) : ConsoleMessagePart(MessagePart.Title, Title);

public static class ConsoleMessageTitlePartFactory
{
    public static ConsoleMessagePart CreateTitlePart(string title) => new ConsoleMessageTitlePart(title);
}
