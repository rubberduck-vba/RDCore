using System.Reflection;
using RDCore.CLI.App.Messages.Model;
using RDCore.CLI.Themes.Model;
using Spectre.Console;

namespace RDCore.CLI.App.Messages;

/// <summary>
/// Renders a <see cref="ConsoleMessageBuilder"/> through <see cref="IAnsiConsole"/> (Spectre.Console).
/// The structured part model is kept; only the rendering back-end changes. Content strings are markup-
/// escaped; style comes from the message <see cref="MessageKind"/> (and the theme icon when one is set).
/// </summary>
public sealed class SpectreConsoleMessageWriter(IAnsiConsole console, IAppThemeService themes) : IConsoleMessageWriter
{
    // TODO: fold these into the theme once it moves to Spectre — style is a Spectre token
    // (named colour, #hex, or "fg on bg"), not a ConsoleColor, so themes can use the full palette.
    private static (string Glyph, string Style) Face(MessageKind kind) => kind switch
    {
        MessageKind.Information => ("ℹ", "blue"),
        MessageKind.Warning => ("▲", "yellow"),
        MessageKind.Error => ("✖", "red"),
        MessageKind.Success => ("✔", "green"),
        _ => ("·", "grey"),
    };

    // the ConsoleColor override channel is transitional; a Spectre style token (any palette) is the
    // target once callers/themes stop speaking ConsoleColor.
    private static string? StyleToken(string? raw)
        => string.IsNullOrWhiteSpace(raw) ? null : raw.ToLowerInvariant();

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

    public IConsoleMessageWriter WriteMessage(ConsoleMessageBuilder builder, ConsoleColor? color = default)
    {
        var (glyph, kindStyle) = Face(builder.Kind);
        var themeIcon = themes.Theme.GetMessageIcon(builder.Kind);
        var icon = string.IsNullOrWhiteSpace(themeIcon) ? glyph : themeIcon;
        var accent = StyleToken(color?.ToString()) ?? kindStyle;

        var timestamp = builder.Parts.OfType<ConsoleMessageTimestampPart>().FirstOrDefault();
        var title = builder.Parts.OfType<ConsoleMessageTitlePart>().FirstOrDefault();
        var body = builder.Parts.OfType<ConsoleMessageBodyPart>().FirstOrDefault();
        var placeholders = builder.Parts.OfType<ConsoleMessageStringLiteralPlaceholderPart>().ToArray();

        var head = new List<string>();
        if (timestamp is { Value.Length: > 0 })
        {
            head.Add($"[grey]{Markup.Escape(timestamp.Value)}[/]");
        }
        head.Add($"[{accent}]{Markup.Escape(icon)}[/]");
        if (title is { Value.Length: > 0 })
        {
            head.Add($"[bold {accent}]{Markup.Escape(title.Value)}[/]");
        }
        console.MarkupLine(string.Join(' ', head));

        if (body is { Body.Length: > 0 })
        {
            var text = Substitute(body.Body, placeholders);
            var bodyToken = StyleToken(body.ColorOverride);
            console.MarkupLine(bodyToken is null ? $"  {text}" : $"  [{bodyToken}]{text}[/]");
        }

        foreach (var verbose in builder.Parts.OfType<ConsoleMessageVerbosePart>().Where(part => part.Value.Length > 0))
        {
            console.MarkupLine($"  [grey]{Markup.Escape(verbose.Value)}[/]");
        }

        foreach (var trace in builder.Parts.OfType<ConsoleMessageStackTracePart>().Where(part => part.Value.Length > 0))
        {
            console.MarkupLine($"[grey]{Markup.Escape(trace.Value)}[/]");
        }

        if (builder.IsWithLineBreak)
        {
            console.WriteLine();
        }
        return this;
    }

    // markup-escape the literal text, then splice each {$PLACEHOLDER} back in as a bold span.
    private static string Substitute(string text, IReadOnlyList<ConsoleMessageStringLiteralPlaceholderPart> placeholders)
    {
        var result = Markup.Escape(text);
        foreach (var placeholder in placeholders)
        {
            result = result.Replace(placeholder.Placeholder, $"[bold]{Markup.Escape(placeholder.Value)}[/]");
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
