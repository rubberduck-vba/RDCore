using RDCore.CLI.App.Messages;
using RDCore.SDK.Model.Errors;

namespace RDCore.CLI.App.Messages.Model;

internal record class ConsoleMessageTitlePart(string Title) : ConsoleMessagePart(MessagePart.Title, Title) { }
internal record class ConsoleMessageTitlePartFactory
{
    public static ConsoleMessagePart CreateTitlePart(string title) => new ConsoleMessageTitlePart(title);
    public static ConsoleMessagePart CreateTitlePart(Exception exception) => new ConsoleMessageTitlePart(ExceptionFormatter.FormatTitle(exception));
    public static ConsoleMessagePart CreateTitlePart(VBRuntimeErrorException exception) => new ConsoleMessageTitlePart(ExceptionFormatter.FormatTitle(exception));
    public static ConsoleMessagePart CreateTitlePart(VBCompileErrorException exception) => new ConsoleMessageTitlePart(ExceptionFormatter.FormatTitle(exception));
}
