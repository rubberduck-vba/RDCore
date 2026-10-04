namespace RDCore.CLI.App.Repl.Commands;

/// <summary>
/// <c>EXIT</c>: ends the session and exits the process — which tears the whole platform down with
/// it, since the language server and everything it owns are children of this connection.
/// </summary>
internal sealed class ExitReplCommand : IReplCommand
{
    public string Name => ReplCommandNames.Exit;
    public IReadOnlyList<string> Aliases => [ReplCommandNames.Quit, ReplCommandNames.Bye];
    public string Summary => Resources.Repl_Exit_Summary;

    public async Task<ReplCommandResult> ExecuteAsync(ReplCommandContext context, string arguments, CancellationToken token)
    {
        await context.Document.CloseAsync(token);
        return ReplCommandResult.Exit;
    }
}
