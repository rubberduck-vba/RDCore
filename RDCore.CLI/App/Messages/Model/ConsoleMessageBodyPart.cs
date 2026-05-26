namespace RDCore.CLI.App.Messages.Model;

internal record class ConsoleMessageBodyPart(string Body) : ConsoleMessagePart(MessagePart.Body, Body) { }
internal record class ConsoleMessageBodyPartFactory
{
    public static ConsoleMessagePart CreateMessageBodyPart(string body) => new ConsoleMessageBodyPart(body);
    public static ConsoleMessagePart CreateMessageBodyPart(Exception exception) => new ConsoleMessageBodyPart(exception.Message);
}

