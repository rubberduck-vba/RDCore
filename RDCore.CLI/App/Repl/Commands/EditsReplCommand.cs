using RDCore.SDK.Client;
using RDCore.SDK.ConsoleIO.Model;

namespace RDCore.CLI.App.Repl.Commands;

/// <summary>
/// <c>EDITS</c>: says which edits made while the program waits are not applied to it - what <c>CONT</c> and <c>STEP</c> would be refused for - each by its name
/// and why.
/// </summary>
/// <remarks>
/// The program is the one as it is typed: the shell tells the language server of the lines edited before any command runs. An edit that applies is not listed,
/// for there is nothing to say of it: the program goes on with it.
/// </remarks>
internal sealed class EditsReplCommand : IReplCommand
{
    public string Name => ReplCommandNames.Edits;
    public IReadOnlyList<string> Aliases => [];
    public string Summary => Resources.Repl_Edits_Summary;

    public async Task<ReplCommandResult> ExecuteAsync(ReplCommandContext context, string arguments, CancellationToken token)
    {
        if (!context.Platform.Provides<ProgramDebugging>())
        {
            context.Console.WriteLine(Resources.Repl_Edits_NoProgram);
            return ReplCommandResult.Continue;
        }

        var status = await context.Platform.GetEditStatusAsync(token);
        if (!status.Suspended)
        {
            context.Console.WriteLine(Resources.Repl_Edits_NoProgram);
        }
        else if (status.Edits.IsDefaultOrEmpty)
        {
            context.Console.WriteLine(Resources.Repl_Edits_None);
        }
        else
        {
            context.Console.WriteMessage(MessageKind.Warning, Resources.Repl_EditsNotApplied);
            ReplExecution.WriteEdits(context.Console, status.Edits);
            context.Console.WriteLine(Resources.Repl_RunToStartAgain);
        }

        return ReplCommandResult.Continue;
    }
}
