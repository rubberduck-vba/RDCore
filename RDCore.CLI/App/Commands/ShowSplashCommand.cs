using RDCore.CLI.Themes;
using RDCore.SDK.ConsoleIO;

namespace RDCore.CLI.App.Commands;

internal readonly record struct SplashArgs(
    bool Show = true);

internal record class ShowSplashCommand : CLICommand<SplashArgs>
{
    private readonly IConsoleMessageWriter _writer;
    private readonly IAppThemeService _themes;

    public ShowSplashCommand(IConsoleMessageWriter writer, IAppThemeService themes) : base("slash")
    {
        _writer = writer;
        _themes = themes;
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

        // the banner art is pre-formatted — print it raw so it is not wrapped or re-flowed; colour
        // via Console (nearest 16, C64-appropriate) rather than Spectre markup which lays out text.
        WriteRaw(logo, theme.SplashLogoColor);
        WriteRaw(Resources.RDCoreSplash_Foreground, theme.SplashTitleColor);

        _writer.WriteSlogan();
    }

    private static void WriteRaw(string art, ConsoleColor color)
    {
        var previous = System.Console.ForegroundColor;
        try
        {
            System.Console.ForegroundColor = color;
            System.Console.Out.WriteLine(art);
        }
        finally
        {
            System.Console.ForegroundColor = previous;
        }
    }
}
