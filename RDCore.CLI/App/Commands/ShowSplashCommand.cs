using RDCore.CLI.Themes;
using RDCore.SDK.ConsoleIO;

namespace RDCore.CLI.App.Commands;

internal readonly record struct SplashArgs(
    bool Show = true);

internal record class ShowSplashCommand : CLICommand<SplashArgs>
{
    /// <summary>How far in the background art sits, so the title art overlays it where it is meant to.</summary>
    private const int SplashIndent = 15;

    private readonly IConsoleMessageWriter _writer;
    private readonly IAppThemeService _themes;
    private readonly IConsoleShellFrame _frame;

    public ShowSplashCommand(IConsoleMessageWriter writer, IAppThemeService themes, IConsoleShellFrame frame) : base("slash")
    {
        _writer = writer;
        _themes = themes;
        _frame = frame;
    }

    public override void Execute(SplashArgs args)
    {
        if (!args.Show)
        {
            return;
        }

        var theme = _themes.Theme;

        // split on '\n' rather than on Environment.NewLine: a .resx value carries LF line endings whatever
        // the platform, because XML normalises them - so splitting on CRLF found nothing to split, and only
        // the first line of the art ever got the indent, which is what made the top of it drift. WriteArt
        // splits the same way, so the two now agree about where a line ends.
        var logo = string.Join('\n', Resources.RDCoreSplash_Background
            .Split('\n')
            .Select(line => $"{new string(' ', SplashIndent)}{line.TrimEnd('\r')}"));

        _writer.WriteAssemblyInfo().WriteLegalNotice();

        // the banner art is pre-formatted — the shell frame prints it raw, one line at a time, so it
        // is never wrapped or re-flowed and every line keeps the frame's background to its full width.
        _frame.WriteArt(logo, theme.SplashLogoColor);
        _frame.WriteArt(Resources.RDCoreSplash_Foreground, theme.SplashTitleColor);

        _writer.WriteSlogan();
    }
}
