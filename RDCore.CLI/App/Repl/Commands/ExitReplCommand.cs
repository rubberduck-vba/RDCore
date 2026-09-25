namespace RDCore.CLI.App.Repl.Commands;

/// <summary>
/// <c>EXIT</c>: ends the session and exits the process — which tears the whole platform down with
/// it, since the language server and everything it owns are children of this connection.
/// </summary>
internal sealed class ExitReplCommand : IReplCommand
{
    public string Name => "EXIT";
    public IReadOnlyList<string> Aliases => ["QUIT", "BYE"];
    public string Summary => Resources.Repl_Exit_Summary;

    public Task<ReplCommandResult> ExecuteAsync(ReplCommandContext context, string arguments, CancellationToken token)
        => Task.FromResult(ReplCommandResult.Exit);
}
