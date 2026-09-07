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

    /// <summary>The shell background, as the nearest <see cref="ConsoleColor"/> (for the console frame).</summary>
    public ConsoleColor ShellBackground => NearestConsoleColor(Resolve(document.Shell.Background));

    /// <summary>The shell foreground, as the nearest <see cref="ConsoleColor"/> (for the console frame).</summary>
    public ConsoleColor ShellForeground => NearestConsoleColor(Resolve(document.Shell.Foreground));

    /// <summary>The resolved style token for the splash logo art.</summary>
    public string SplashLogo => Resolve(document.Splash.Logo);

    /// <summary>The resolved style token for the splash title.</summary>
    public string SplashTitle => Resolve(document.Splash.Title);

    /// <summary>The splash logo colour as the nearest <see cref="ConsoleColor"/> (the art is printed raw, unwrapped).</summary>
    public ConsoleColor SplashLogoColor => NearestConsoleColor(SplashLogo);

    /// <summary>The splash title colour as the nearest <see cref="ConsoleColor"/>.</summary>
    public ConsoleColor SplashTitleColor => NearestConsoleColor(SplashTitle);

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

    private static readonly (ConsoleColor Color, byte R, byte G, byte B)[] _consolePalette =
    [
        (ConsoleColor.Black, 0, 0, 0), (ConsoleColor.DarkBlue, 0, 0, 128), (ConsoleColor.DarkGreen, 0, 128, 0),
        (ConsoleColor.DarkCyan, 0, 128, 128), (ConsoleColor.DarkRed, 128, 0, 0), (ConsoleColor.DarkMagenta, 128, 0, 128),
        (ConsoleColor.DarkYellow, 128, 128, 0), (ConsoleColor.Gray, 192, 192, 192), (ConsoleColor.DarkGray, 128, 128, 128),
        (ConsoleColor.Blue, 0, 0, 255), (ConsoleColor.Green, 0, 255, 0), (ConsoleColor.Cyan, 0, 255, 255),
        (ConsoleColor.Red, 255, 0, 0), (ConsoleColor.Magenta, 255, 0, 255), (ConsoleColor.Yellow, 255, 255, 0),
        (ConsoleColor.White, 255, 255, 255),
    ];

    private static ConsoleColor NearestConsoleColor(string token)
    {
        Color rgb;
        try
        {
            rgb = Style.Parse(token).Foreground;
        }
        catch (Exception)
        {
            return ConsoleColor.Black;
        }

        var best = ConsoleColor.Black;
        var bestDistance = int.MaxValue;
        foreach (var (color, r, g, b) in _consolePalette)
        {
            var dr = rgb.R - r;
            var dg = rgb.G - g;
            var db = rgb.B - b;
            var distance = (dr * dr) + (dg * dg) + (db * db);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = color;
            }
        }
        return best;
    }
}
