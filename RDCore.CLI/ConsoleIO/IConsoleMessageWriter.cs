using RDCore.CLI.ConsoleIO.Model;

namespace RDCore.CLI.ConsoleIO;

public interface IConsoleMessageWriter
{
    IConsoleMessageWriter Clear();

    IConsoleMessageWriter WriteMessage(ConsoleMessageBuilder builder);

    IConsoleMessageWriter WriteException(Exception exception);

    IConsoleMessageWriter WriteAssemblyInfo();

    // the slogan, on a line of its own, this far in.
    IConsoleMessageWriter WriteSlogan(int indent = 0);

    IConsoleMessageWriter WriteLegalNotice();

    IConsoleMessageWriter WriteLine(string text)
        => WriteMessage(new ConsoleMessageBuilder().WithKind(MessageKind.Trace).WithMessageBody(text));

    IConsoleMessageWriter Message(MessageKind kind, string message, string? title = default)
    {
        var builder = new ConsoleMessageBuilder().WithKind(kind).WithMessageBody(message);
        return WriteMessage(title is null ? builder : builder.WithTitle(title));
    }

    IConsoleMessageWriter Info(string message, string? title = default) => Message(MessageKind.Information, message, title);

    IConsoleMessageWriter Success(string message, string? title = default) => Message(MessageKind.Success, message, title);

    IConsoleMessageWriter Warn(string message, string? title = default) => Message(MessageKind.Warning, message, title);

    IConsoleMessageWriter Error(string message, string? title = default) => Message(MessageKind.Error, message, title);
}
