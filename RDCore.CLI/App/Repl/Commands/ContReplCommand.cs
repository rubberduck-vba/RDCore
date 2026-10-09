using RDCore.SDK.Client;
using RDCore.SDK.ConsoleIO.Model;

namespace RDCore.CLI.App.Repl.Commands;

/// <summary>
/// <c>CONT</c>: goes on with the program that was stopped.
/// </summary>
/// <remarks>
/// From where it stopped, or from a line: <c>CONT 200</c> moves the point the program goes on from to line 200 first, without running anything in between
/// (<strong>MS-VBAL 5.4.2.11</strong>: a program that is suspended is suspended where it is, and the person looking at it may want it elsewhere). The line is one of the
/// procedure the program waits in, which for a shell is the program.
/// </remarks>
internal sealed class ContReplCommand : IReplCommand
{
    public string Name => ReplCommandNames.Cont;
    public IReadOnlyList<string> Aliases => [ReplCommandNames.Continue];
    public string Summary => Resources.Repl_Cont_Summary;

    public async Task<ReplCommandResult> ExecuteAsync(ReplCommandContext context, string arguments, CancellationToken token)
    {
        if (context.Debugger.State is not ReplDebugState.Suspended || !context.Platform.Provides<ProgramDebugging>())
        {
            context.Console.WriteMessage(MessageKind.Error, Resources.Repl_CantContinue);
            return ReplCommandResult.Continue;
        }

        var text = arguments.Trim();
        if (text.Length > 0)
        {
            if (!int.TryParse(text, out var number))
            {
                context.Console.WriteMessage(MessageKind.Error, Resources.Repl_SyntaxError, arguments);
                return ReplCommandResult.Continue;
            }

            if (context.Program.IndexOf(number) < 0)
            {
                context.Console.WriteMessage(MessageKind.Error, Resources.Repl_UndefinedLine, number.ToString());
                return ReplCommandResult.Continue;
            }

            var moved = await context.Platform.GotoAsync(number.ToString(), token);
            if (!moved.Moved)
            {
                context.Console.WriteMessage(MessageKind.Error, Resources.Repl_CantContinue, moved.Reason);
                return ReplCommandResult.Continue;
            }
        }

        await ReplExecution.ResumeAsync(context, step: null);
        return ReplCommandResult.Continue;
    }
}
