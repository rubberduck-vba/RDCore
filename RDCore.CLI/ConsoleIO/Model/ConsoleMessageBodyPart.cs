namespace RDCore.CLI.ConsoleIO.Model;

public record class ConsoleMessageBodyPart(string Body) : ConsoleMessagePart(MessagePart.Body, Body);

public static class ConsoleMessageBodyPartFactory
{
    public static ConsoleMessagePart CreateMessageBodyPart(string body) => new ConsoleMessageBodyPart(body);

    public static ConsoleMessagePart CreateMessageBodyPart(Exception exception) => new ConsoleMessageBodyPart(exception.Message);
}
