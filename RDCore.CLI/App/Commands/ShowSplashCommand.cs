using RDCore.CLI.Themes;
using RDCore.SDK.ConsoleIO;

namespace RDCore.CLI.App.Commands;

internal readonly record struct SplashArgs(
    bool Show = true);

internal record class ShowSplashCommand : CLICommand<SplashArgs>
{
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
        var logo = string.Join(Environment.NewLine, Resources.RDCoreSplash_Background
            .Split(Environment.NewLine)
            .Select(line => $"{new string(' ', 15)}{line}"));

        _writer.WriteAssemblyInfo().WriteLegalNotice();

        // the banner art is pre-formatted — the shell frame prints it raw, one line at a time, so it
        // is never wrapped or re-flowed and every line keeps the frame's background to its full width.
        _frame.WriteArt(logo, theme.SplashLogoColor);
        _frame.WriteArt(Resources.RDCoreSplash_Foreground, theme.SplashTitleColor);

        _writer.WriteSlogan();
    }
}
