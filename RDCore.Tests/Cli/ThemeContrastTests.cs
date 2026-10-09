using RDCore.CLI.Themes;
using RDCore.SDK.ConsoleIO.Model;
using Spectre.Console;
using System.Reflection;
using System.Text.Json;

namespace RDCore.Tests.Cli;

/// <summary>
/// A theme is written for one shell background, and a colour it names that is hardly distinguishable from that background is text nobody can read: the light theme
/// had white numbers on a white shell.
/// </summary>
[TestClass]
public sealed class ThemeContrastTests
{
    // WCAG 2.x: 3 is the least for large text, and a shell is mostly small; this is below both, and what it catches is a colour that is the background's own.
    private const double MinimumContrast = 2.0;

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static IEnumerable<string> ThemeResources
        => typeof(AppTheme).Assembly.GetManifestResourceNames().Where(name => name.EndsWith(".theme", StringComparison.OrdinalIgnoreCase));

    private static AppTheme Load(string resource)
    {
        using var stream = typeof(AppTheme).Assembly.GetManifestResourceStream(resource)!;
        return new AppTheme(JsonSerializer.Deserialize<ThemeDocument>(stream, Json)!);
    }

    private static double Luminance(Color color)
    {
        static double Channel(byte value)
        {
            var c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }

    private static double Contrast(Color a, Color b)
    {
        var (high, low) = (Math.Max(Luminance(a), Luminance(b)), Math.Min(Luminance(a), Luminance(b)));
        return (high + 0.05) / (low + 0.05);
    }

    [TestMethod]
    public void ThereAreThemesToCheck() => Assert.IsGreaterThanOrEqualTo(4, ThemeResources.Count());

    [TestMethod]
    [DynamicData(nameof(ThemeResources))]
    public void EveryColourOfATheme_CanBeReadOnItsShellBackground(string resource)
    {
        var theme = Load(resource);
        var background = new Color(theme.ShellBackground.R, theme.ShellBackground.G, theme.ShellBackground.B);

        var tokens = new Dictionary<string, string>
        {
            ["shell foreground"] = $"#{theme.ShellForeground.R:x2}{theme.ShellForeground.G:x2}{theme.ShellForeground.B:x2}",
            ["keyword"] = theme.Syntax.Keyword,
            ["comment"] = theme.Syntax.Comment,
            ["string"] = theme.Syntax.String,
            ["number"] = theme.Syntax.Number,
            ["identifier"] = theme.Syntax.Identifier,
            ["identifier-class"] = theme.Syntax.IdentifierClass,
            ["identifier-const"] = theme.Syntax.IdentifierConst,
            ["splash logo"] = theme.Splash.Logo,
            ["splash title"] = theme.Splash.Title,
            ["breakpoint mark"] = theme.Debug.BreakpointGlyph,
        };
        foreach (var kind in Enum.GetValues<MessageKind>())
        {
            tokens[$"{kind} title"] = theme.GetStyle(kind, MessagePart.Title);
            tokens[$"{kind} body"] = theme.GetStyle(kind, MessagePart.Body);
        }

        var unreadable = tokens
            .Select(token => (token.Key, Token: token.Value, Ratio: Contrast(Style.Parse(token.Value).Foreground, background)))
            .Where(entry => entry.Ratio < MinimumContrast)
            .Select(entry => $"{entry.Key} ({entry.Token}) is {entry.Ratio:0.0}:1")
            .ToArray();

        Assert.IsEmpty(unreadable, $"{theme.Name}: " + string.Join("; ", unreadable));
    }

    [TestMethod]
    [DynamicData(nameof(ThemeResources))]
    public void ALineOfAListingThatTheDebuggerMarks_CanBeReadOnItsOwnBackground(string resource)
    {
        var theme = Load(resource);
        var shell = new Color(theme.ShellBackground.R, theme.ShellBackground.G, theme.ShellBackground.B);

        var marked = new Dictionary<string, string> { ["breakpoint"] = theme.Debug.Breakpoint, ["current statement"] = theme.Debug.CurrentStatement };
        var unreadable = marked
            .Select(line => (line.Key, Token: line.Value, Style: Style.Parse(line.Value)))
            .Where(line => Contrast(line.Style.Foreground, line.Style.Background) < MinimumContrast)
            .Select(line => $"{line.Key} ({line.Token}) is {Contrast(line.Style.Foreground, line.Style.Background):0.0}:1")
            .ToArray();

        Assert.IsEmpty(unreadable, $"{theme.Name}: " + string.Join("; ", unreadable));

        // and the mark has to stand out from the shell it is drawn on, as the background of the line does.
        foreach (var line in marked)
        {
            Assert.IsGreaterThan(1.2, Contrast(Style.Parse(line.Value).Background, shell), $"{theme.Name}: the background of the {line.Key} line is the shell's own");
        }
    }
}
