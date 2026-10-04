namespace RDCore.CLI.App.Repl.Commands;

internal record class ClearReplCommand : IReplCommand
{
    public string Name => ReplCommandNames.Clear;

    public IReadOnlyList<string> Aliases => [ReplCommandNames.Cls];

    public string Summary => Resources.Repl_Clear_Summary;

    public async Task<ReplCommandResult> ExecuteAsync(ReplCommandContext context, string arguments, CancellationToken token)
    {
        context.Console.Clear();
        return ReplCommandResult.Continue;
    }
}
