using RDCore.SDK.ConsoleIO.Model;

namespace RDCore.SDK.ConsoleIO;

/// <summary>
/// Renders structured console messages. The full form takes a <see cref="ConsoleMessageBuilder"/>;
/// the convenience methods cover the common "one styled line" cases. Every method returns the writer
/// for chaining.
/// </summary>
public interface IConsoleMessageWriter
{
    /// <summary>Clears the console.</summary>
    IConsoleMessageWriter Clear();

    /// <summary>
    /// Renders a fully built message.
    /// </summary>
    /// <param name="builder">
    /// The message parts — kind, and any of: title, timestamp, body, verbose, stack trace, placeholder spans.
    /// </param>
    IConsoleMessageWriter WriteMessage(ConsoleMessageBuilder builder);

    /// <summary>Renders an exception (type, message, and a shortened stack trace).</summary>
    IConsoleMessageWriter WriteException(Exception exception);

    /// <summary>Writes the running assembly's name and version line.</summary>
    IConsoleMessageWriter WriteAssemblyInfo();

    /// <summary>Writes the project slogan line.</summary>
    IConsoleMessageWriter WriteSlogan();

    /// <summary>Writes the copyright / legal notice line.</summary>
    IConsoleMessageWriter WriteLegalNotice();

    /// <summary>
    /// Writes a single plain line at <see cref="MessageKind.Trace"/> — no title, timestamp, or icon.
    /// </summary>
    /// <param name="text">The line to write.</param>
    IConsoleMessageWriter WriteLine(string text)
        => WriteMessage(new ConsoleMessageBuilder().WithKind(MessageKind.Trace).WithMessageBody(text));

    /// <summary>
    /// Writes <paramref name="message"/> as a message of the given <paramref name="kind"/>, optionally titled.
    /// </summary>
    /// <param name="kind">The message kind, which selects the icon and accent style.</param>
    /// <param name="message">The message body.</param>
    /// <param name="title">An optional title shown ahead of the body.</param>
    IConsoleMessageWriter Message(MessageKind kind, string message, string? title = default)
    {
        var builder = new ConsoleMessageBuilder().WithKind(kind).WithMessageBody(message);
        return WriteMessage(title is null ? builder : builder.WithTitle(title));
    }

    /// <summary>Writes <paramref name="message"/> as an <see cref="MessageKind.Information"/> message, optionally titled.</summary>
    IConsoleMessageWriter Info(string message, string? title = default) => Message(MessageKind.Information, message, title);

    /// <summary>Writes <paramref name="message"/> as a <see cref="MessageKind.Success"/> message, optionally titled.</summary>
    IConsoleMessageWriter Success(string message, string? title = default) => Message(MessageKind.Success, message, title);

    /// <summary>Writes <paramref name="message"/> as a <see cref="MessageKind.Warning"/> message, optionally titled.</summary>
    IConsoleMessageWriter Warn(string message, string? title = default) => Message(MessageKind.Warning, message, title);

    /// <summary>Writes <paramref name="message"/> as an <see cref="MessageKind.Error"/> message, optionally titled.</summary>
    IConsoleMessageWriter Error(string message, string? title = default) => Message(MessageKind.Error, message, title);
}
