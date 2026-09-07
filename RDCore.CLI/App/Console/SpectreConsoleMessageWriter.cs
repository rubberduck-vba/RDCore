using System.Reflection;
using RDCore.CLI.Themes;
using RDCore.SDK.ConsoleIO;
using RDCore.SDK.ConsoleIO.Model;
using Spectre.Console;

namespace RDCore.CLI.App.Console;

/// <summary>
/// Renders a <see cref="ConsoleMessageBuilder"/> through <see cref="IAnsiConsole"/> (Spectre.Console),
/// styling each part with the active <see cref="AppTheme"/>. Content strings are markup-escaped.
/// </summary>
public sealed class SpectreConsoleMessageWriter(IAnsiConsole console, IAppThemeService themes) : IConsoleMessageWriter
{
    // used only when the theme supplies no icon for a kind.
    private static string GlyphFallback(MessageKind kind) => kind switch
    {
        MessageKind.Information => "ℹ",
        MessageKind.Warning => "▲",
        MessageKind.Error => "✖",
        MessageKind.Success => "✔",
        _ => "·",
    };

    public IConsoleMessageWriter Clear()
    {
        console.Clear();
        return this;
    }

    public IConsoleMessageWriter WriteException(Exception exception)
    {
        console.WriteException(exception, ExceptionFormats.ShortenPaths | ExceptionFormats.ShortenTypes);
        return this;
    }

    public IConsoleMessageWriter WriteMessage(ConsoleMessageBuilder builder)
    {
        var kind = builder.Kind;
        var theme = themes.Theme;
        var icon = theme.GetIcon(kind) is { Length: > 0 } themed ? themed : GlyphFallback(kind);

        var timestamp = builder.Parts.OfType<ConsoleMessageTimestampPart>().FirstOrDefault();
        var title = builder.Parts.OfType<ConsoleMessageTitlePart>().FirstOrDefault();
        var body = builder.Parts.OfType<ConsoleMessageBodyPart>().FirstOrDefault();
        var placeholders = builder.Parts.OfType<ConsoleMessageStringLiteralPlaceholderPart>().ToArray();

        var head = new List<string>();
        if (timestamp is { Value.Length: > 0 })
        {
            head.Add($"[{theme.GetStyle(kind, MessagePart.Timestamp)}]{Markup.Escape(timestamp.Value)}[/]");
        }
        head.Add($"[{theme.GetStyle(kind, MessagePart.Title)}]{Markup.Escape(icon)}[/]");
        if (title is { Value.Length: > 0 })
        {
            head.Add($"[bold {theme.GetStyle(kind, MessagePart.Title)}]{Markup.Escape(title.Value)}[/]");
        }
        console.MarkupLine(string.Join(' ', head));

        if (body is { Body.Length: > 0 })
        {
            console.MarkupLine($"  [{theme.GetStyle(kind, MessagePart.Body)}]{Substitute(kind, theme, body.Body, placeholders)}[/]");
        }

        foreach (var verbose in builder.Parts.OfType<ConsoleMessageVerbosePart>().Where(part => part.Value.Length > 0))
        {
            console.MarkupLine($"  [{theme.GetStyle(kind, MessagePart.Verbose)}]{Markup.Escape(verbose.Value)}[/]");
        }

        foreach (var trace in builder.Parts.OfType<ConsoleMessageStackTracePart>().Where(part => part.Value.Length > 0))
        {
            console.MarkupLine($"[{theme.GetStyle(kind, MessagePart.StackTrace)}]{Markup.Escape(trace.Value)}[/]");
        }

        if (builder.IsWithLineBreak)
        {
            console.WriteLine();
        }
        return this;
    }

    // markup-escape the literal text, then splice each {$PLACEHOLDER} back in as a metric-styled span.
    private static string Substitute(MessageKind kind, AppTheme theme, string text, IReadOnlyList<ConsoleMessageStringLiteralPlaceholderPart> placeholders)
    {
        var metric = theme.GetStyle(kind, MessagePart.Metric);
        var result = Markup.Escape(text);
        foreach (var placeholder in placeholders)
        {
            result = result.Replace(placeholder.Placeholder, $"[{metric}]{Markup.Escape(placeholder.Value)}[/]");
        }
        return result;
    }

    public IConsoleMessageWriter WriteAssemblyInfo()
    {
        var name = Assembly.GetExecutingAssembly().GetName();
        var company = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyCompanyAttribute>()?.Company ?? string.Empty;
        return WriteMessage(new ConsoleMessageBuilder()
            .WithKind(MessageKind.Trace)
            .WithMessageBody($"{name.Name} [v{name.Version?.ToString(3) ?? "0.1a"}]")
            .WithPlaceholder("COMPANY", company));
    }

    public IConsoleMessageWriter WriteLegalNotice()
    {
        var company = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyCompanyAttribute>()?.Company ?? string.Empty;
        return WriteMessage(new ConsoleMessageBuilder()
            .WithKind(MessageKind.Trace)
            .WithMessageBody(Resources.CopyrightNotice.Replace("{$YEAR}", DateTimeOffset.UtcNow.Year.ToString()))
            .WithPlaceholder("COMPANY", company)
            .WithLineBreak());
    }

    public IConsoleMessageWriter WriteSlogan() => WriteMessage(new ConsoleMessageBuilder()
        .WithKind(MessageKind.Information)
        .WithMessageBody(Resources.RDCore_Slogan)
        .WithPlaceholder("VIVAT", "V I V A T")
        .WithPlaceholder("CUCUMIS", "C U C U M I S")
        .WithLineBreak());
}
