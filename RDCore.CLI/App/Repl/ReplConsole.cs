using RDCore.SDK.ConsoleIO;
using RDCore.SDK.ConsoleIO.Model;

namespace RDCore.CLI.App.Repl;

/// <summary>
/// The shell's console: plain lines go straight out in the shell frame's own colours, themed
/// messages go through the platform's console renderer.
/// </summary>
/// <remarks>
/// Program output and listings are the shell's own voice and must look like the machine talking, not
/// like a log record — no icon, no accent colour, no indent. A message is the platform talking, and
/// the theme should mark it, so those route to <see cref="IConsoleMessageWriter"/> like every other
/// platform message.
/// </remarks>
/// <param name="writer">The platform's themed console renderer.</param>
internal sealed class ReplConsole(IConsoleMessageWriter writer) : IReplConsole
{
    /// <inheritdoc/>
    public void WriteLine(string text = "") => System.Console.Out.WriteLine(text);

    /// <inheritdoc/>
    public void WriteMessage(MessageKind kind, string message, string? verbose = null)
    {
        var builder = new ConsoleMessageBuilder().WithKind(kind).WithMessageBody(message);
        writer.WriteMessage(verbose is { Length: > 0 } ? builder.WithVerbose(verbose) : builder);
    }
}
