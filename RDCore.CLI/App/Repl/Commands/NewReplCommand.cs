namespace RDCore.CLI.App.Repl.Commands;

/// <summary>
/// <c>NEW</c>: clears the program buffer. Says nothing on success, as its BASIC namesake does not.
/// </summary>
internal sealed class NewReplCommand : IReplCommand
{
    public string Name => ReplCommandNames.New;
    public IReadOnlyList<string> Aliases => [];
    public string Summary => Resources.Repl_New_Summary;

    public async Task<ReplCommandResult> ExecuteAsync(ReplCommandContext context, string arguments, CancellationToken token)
    {
        // a new program is not the file that was loaded: the language server is told the document is closed, not that it was emptied.
        await context.Document.CloseAsync(token);
        context.Program.Clear();
        return ReplCommandResult.Continue;
    }
}
