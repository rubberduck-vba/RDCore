using RDCore.CLI.Themes;
using RDCore.SDK.ConsoleIO;
using RDCore.SDK.ConsoleIO.Model;
using Spectre.Console;

namespace RDCore.CLI.App.Repl;

/// <summary>
/// The shell's console: plain lines go straight out in the shell frame's own colours, themed
/// messages go through the platform's console renderer.
/// </summary>
/// <remarks>
/// Program output and listings are the shell's own voice and must look like the machine talking, not
/// like a log record — no icon, no accent colour, no indent. A message is the platform talking, and
/// the theme should mark it, so those route to <see cref="IConsoleMessageWriter"/> like every other
/// platform message. A line of a listing is the exception that is both: its text is plain, and the colours of
/// its keywords, literals and names are the theme's.
/// </remarks>
/// <param name="writer">The platform's themed console renderer.</param>
/// <param name="console">The console a styled line is written through.</param>
/// <param name="themes">The themes, which say how each style of a listing looks.</param>
internal sealed class ReplConsole(IConsoleMessageWriter writer, IAnsiConsole console, IAppThemeService themes) : IReplConsole
{
    /// <inheritdoc/>
    public void WriteLine(string text = "") => System.Console.Out.WriteLine(text);

    /// <inheritdoc/>
    public void WriteLine(IReadOnlyList<ReplTextRun> runs)
    {
        var syntax = themes.Theme.Syntax;
        var markup = string.Concat(runs.Select(run => StyleOf(run.Style, syntax) is { } style
            ? $"[{style}]{Markup.Escape(run.Text)}[/]"
            : Markup.Escape(run.Text)));
        console.MarkupLine(markup);
    }

    /// <inheritdoc/>
    public void WriteMessage(MessageKind kind, string message, string? verbose = null)
    {
        var builder = new ConsoleMessageBuilder().WithKind(kind).WithMessageBody(message);
        writer.WriteMessage(verbose is { Length: > 0 } ? builder.WithVerbose(verbose) : builder);
    }

    // a style the theme does not name is the plain one, which is what the shell is written in already.
    private static string? StyleOf(ReplTextStyle style, ThemeSyntaxStyles syntax) => style switch
    {
        ReplTextStyle.Keyword => syntax.Keyword,
        ReplTextStyle.Comment => syntax.Comment,
        ReplTextStyle.String => syntax.String,
        ReplTextStyle.Number => syntax.Number,
        ReplTextStyle.Identifier => syntax.Identifier,
        ReplTextStyle.IdentifierClass => syntax.IdentifierClass,
        ReplTextStyle.IdentifierConst => syntax.IdentifierConst,
        _ => null,
    };
}
