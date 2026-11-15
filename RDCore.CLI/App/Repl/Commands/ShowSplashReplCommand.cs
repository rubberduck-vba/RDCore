namespace RDCore.CLI.App.Repl.Commands;

/// <summary>
/// Shows the shell's splash (<see cref="ReplSplash"/>) again: the one the shell showed as it started.
/// </summary>
internal sealed class ShowSplashReplCommand(ReplSplash splash) : IReplCommand
{
    public string Name => ReplCommandNames.Splash;

    public IReadOnlyList<string> Aliases => [];

    public string Summary => "Shows the splash screen.";

    public Task<ReplCommandResult> ExecuteAsync(ReplCommandContext context, string arguments, CancellationToken token)
    {
        splash.Write(context.Console);
        return Task.FromResult(ReplCommandResult.Continue);
    }
}
