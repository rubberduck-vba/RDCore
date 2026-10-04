using RDCore.SDK.ConsoleIO;

namespace RDCore.CLI.App.Repl.Commands;

internal record class ShowSplashReplCommand : IReplCommand
{
    /// <summary>How far in the background art sits, so the title art overlays it where it is meant to.</summary>
    private const int SplashIndent = 15;

    private readonly IConsoleMessageWriter _writer;

    public ShowSplashReplCommand(IConsoleMessageWriter writer)
    {
        _writer = writer;
    }

    public string Name => ReplCommandNames.Splash;

    public IReadOnlyList<string> Aliases => [];

    public string Summary => "Shows the splash screen.";

    public async Task<ReplCommandResult> ExecuteAsync(ReplCommandContext context, string arguments, CancellationToken token)
    {
        // split on '\n' rather than on Environment.NewLine: a .resx value carries LF line endings whatever
        // the platform, because XML normalises them - so splitting on CRLF found nothing to split, and only
        // the first line of the art ever got the indent, which is what made the top of it drift. WriteArt
        // splits the same way, so the two now agree about where a line ends.
        var logo = string.Join('\n', Resources.RDCoreSplash_Background
            .Split('\n')
            .SkipLast(1)
            .Select(line => $"{new string(' ', SplashIndent)}{line.TrimEnd('\r')}"));

        context.Console.WriteLine([new ReplTextRun(logo, ReplTextStyle.Keyword)]);
        context.Console.WriteLine([new ReplTextRun(Resources.RDCoreSplash_Foreground, ReplTextStyle.Number)]);
        _writer.WriteSlogan();
        _writer.WriteLegalNotice();

        return ReplCommandResult.Continue;
    }
}
