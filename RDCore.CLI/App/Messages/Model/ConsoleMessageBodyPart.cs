namespace RDCore.CLI.App.Messages.Model;

internal record class ConsoleMessageBodyPart(string Body, string? ColorOverride = default) : ConsoleMessagePart(MessagePart.Body, Body) { }
internal record class ConsoleMessageBodyPartFactory
{
    public static ConsoleMessagePart CreateMessageBodyPart(string body, string? color = default) => new ConsoleMessageBodyPart(body, color);
    public static ConsoleMessagePart CreateMessageBodyPart(Exception exception) => new ConsoleMessageBodyPart(exception.Message);
}

//internal record class ConsoleMessageOverlayMessagePart(string Body, string Color, int LineStart, int Lines) : ConsoleMessagePart(MessagePart.Overlay, Body) { }
//internal record class ConsoleMessageOverlayMessagePartFactory
//{
//    public static ConsoleMessagePart CreateMessageOverlayBodyPart(string overlay, string color, int lineStart, int lines) => new ConsoleMessageOverlayMessagePart(overlay, color, lineStart, lines);
//}