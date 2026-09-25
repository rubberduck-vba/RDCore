using RDCore.SDK.ConsoleIO;
using RDCore.SDK.ConsoleIO.Model;
using Spectre.Console;

namespace RDCore.CLI.Themes;

/// <summary>Resolved syntax-highlight style tokens for a program-mode source listing.</summary>
public readonly record struct ThemeSyntaxStyles(
    string Keyword, string Comment, string String, string Number,
    string Identifier, string IdentifierClass, string IdentifierConst);

/// <summary>
/// A loaded theme. Wraps a <see cref="ThemeDocument"/> and resolves its palette references to
/// validated Spectre style tokens; supplies the styles the console renderer and the shell frame need.
/// </summary>
public sealed class AppTheme(ThemeDocument document)
{
    /// <summary>The token used when a value neither names a palette entry nor parses as a Spectre style.</summary>
    public const string FallbackToken = "default";

    /// <summary>A minimal built-in theme used when theming is disabled or nothing loaded.</summary>
    public static AppTheme Neutral { get; } = new(new ThemeDocument
    {
        Name = "neutral",
        Messages = new()
        {
            ["trace"] = new() { Icon = "·", Title = "grey", Body = "default", Metric = "default" },
            ["information"] = new() { Icon = "ℹ", Title = "blue", Body = "default", Metric = "blue" },
            ["warning"] = new() { Icon = "▲", Title = "yellow", Body = "yellow", Metric = "yellow" },
            ["error"] = new() { Icon = "✖", Title = "red", Body = "red", Metric = "red" },
            ["success"] = new() { Icon = "✔", Title = "green", Body = "green", Metric = "green" },
        },
    });

    /// <summary>The theme name.</summary>
    public string Name => document.Name;

    /// <summary>The icon glyph for a message kind (may be empty).</summary>
    public string GetIcon(MessageKind kind)
        => document.Messages.TryGetValue(Key(kind), out var style) ? style.Icon : string.Empty;

    /// <summary>The resolved Spectre style token for one part of a message of the given kind.</summary>
    public string GetStyle(MessageKind kind, MessagePart part)
    {
        if (!document.Messages.TryGetValue(Key(kind), out var style))
        {
            return FallbackToken;
        }

        var raw = part switch
        {
            MessagePart.Title => style.Title,
            MessagePart.Verbose => style.Verbose,
            MessagePart.Timestamp => style.Timestamp,
            MessagePart.Metric => style.Metric,
            MessagePart.StackTrace => style.StackTrace,
            _ => style.Body,
        };
        return Resolve(raw);
    }

    /// <summary>The shell background, in 24-bit colour — what the console shell frame is painted with.</summary>
    public ConsoleRgbColor ShellBackground => ToRgb(Resolve(document.Shell.Background));

    /// <summary>The shell foreground, in 24-bit colour — what the console shell frame is painted with.</summary>
    public ConsoleRgbColor ShellForeground => ToRgb(Resolve(document.Shell.Foreground));

    /// <summary>The resolved style token for the splash logo art.</summary>
    public string SplashLogo => Resolve(document.Splash.Logo);

    /// <summary>The resolved style token for the splash title.</summary>
    public string SplashTitle => Resolve(document.Splash.Title);

    /// <summary>The splash logo colour, in 24-bit colour (the art is printed raw, unwrapped).</summary>
    public ConsoleRgbColor SplashLogoColor => ToRgb(SplashLogo);

    /// <summary>The splash title colour, in 24-bit colour.</summary>
    public ConsoleRgbColor SplashTitleColor => ToRgb(SplashTitle);

    /// <summary>The resolved syntax-highlight tokens for program-mode listings.</summary>
    public ThemeSyntaxStyles Syntax => new(
        Resolve(document.Syntax.Keyword), Resolve(document.Syntax.Comment), Resolve(document.Syntax.String),
        Resolve(document.Syntax.Number), Resolve(document.Syntax.Identifier),
        Resolve(document.Syntax.IdentifierClass), Resolve(document.Syntax.IdentifierConst));

    private static string Key(MessageKind kind) => kind.ToString().ToLowerInvariant();

    // a value is a palette reference if the palette has it (resolved recursively, one hop at a time,
    // with a small depth guard against cycles); otherwise it must parse as a Spectre style token.
    private string Resolve(string value, int depth = 0)
    {
        if (depth < 8 && document.Palette.TryGetValue(value, out var mapped))
        {
            return Resolve(mapped, depth + 1);
        }
        return IsValidToken(value) ? value : FallbackToken;
    }

    private static bool IsValidToken(string token)
    {
        try
        {
            _ = Style.Parse(token);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // a resolved token is a Spectre style; its foreground carries the 24-bit value the frame needs,
    // whether the theme wrote it as #rrggbb or as a named colour.
    private static ConsoleRgbColor ToRgb(string token)
    {
        try
        {
            var color = Style.Parse(token).Foreground;
            return new ConsoleRgbColor(color.R, color.G, color.B);
        }
        catch (Exception)
        {
            return ConsoleRgbColor.Black;
        }
    }
}
