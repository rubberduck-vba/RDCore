namespace RDCore.CLI.App.Repl.Commands;

/// <summary>
/// <c>HELP</c>: lists the shell's own commands and what each one does.
/// </summary>
internal sealed class HelpReplCommand : IReplCommand
{
    public string Name => "HELP";
    public IReadOnlyList<string> Aliases => [];
    public string Summary => Resources.Repl_Help_Summary;

    public Task<ReplCommandResult> ExecuteAsync(ReplCommandContext context, string arguments, CancellationToken token)
    {
        var width = context.Commands.Max(command => command.Name.Length);
        foreach (var command in context.Commands)
        {
            context.Console.WriteLine($"  {command.Name.PadRight(width)}  {command.Summary}");
        }

        context.Console.WriteLine();
        return Task.FromResult(ReplCommandResult.Continue);
    }
}
