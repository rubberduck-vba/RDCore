namespace RDCore.CLI.App.Repl.Commands;

/// <summary>
/// <c>NEW</c>: clears the program buffer. Says nothing on success, as its BASIC namesake does not.
/// </summary>
internal sealed class NewReplCommand : IReplCommand
{
    public string Name => "NEW";
    public IReadOnlyList<string> Aliases => [];
    public string Summary => Resources.Repl_New_Summary;

    public Task<ReplCommandResult> ExecuteAsync(ReplCommandContext context, string arguments, CancellationToken token)
    {
        context.Program.Clear();
        return Task.FromResult(ReplCommandResult.Continue);
    }
}
