using RDCore.SDK.Client;
using RDCore.SDK.ConsoleIO.Model;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.CLI.App.Repl.Commands;

/// <summary>
/// <c>EVAL</c>: the value of an expression, in the activation of the program that was stopped that <c>FRAME</c> selected.
/// </summary>
/// <remarks>
/// <c>EVAL k + 1</c> is written as it would be in the procedure the activation is of, and sees its parameters, its locals and its module. A call in it is made, and what it
/// prints is shown; the program waits where it did. It is not a statement: to change a variable, type the assignment.
/// </remarks>
internal sealed class EvalReplCommand : IReplCommand
{
    public string Name => ReplCommandNames.Eval;
    public IReadOnlyList<string> Aliases => [];
    public string Summary => Resources.Repl_Eval_Summary;

    public async Task<ReplCommandResult> ExecuteAsync(ReplCommandContext context, string arguments, CancellationToken token)
    {
        if (context.Debugger.State is not ReplDebugState.Suspended || !context.Platform.Provides<ProgramDebugging>())
        {
            context.Console.WriteMessage(MessageKind.Warning, Resources.Repl_NotStopped);
            return ReplCommandResult.Continue;
        }

        var expression = arguments.Trim();
        if (expression.Length == 0)
        {
            context.Console.WriteMessage(MessageKind.Error, Resources.Repl_SyntaxError, arguments);
            return ReplCommandResult.Continue;
        }

        var result = await context.Platform.EvaluateAsync(context.Debugger.SelectedFrame, expression, token);
        foreach (var line in result.Output)
        {
            context.Console.WriteLine(line);
        }

        if (!result.Success)
        {
            context.Console.WriteMessage(MessageKind.Error, Resources.Repl_Eval_Failed, result.Error);
            return ReplCommandResult.Continue;
        }

        await ReplVariableListing.WriteAsync(context, [new HostVariable(expression, result.Value, result.Type, result.Reference)], indent: 0, token);
        return ReplCommandResult.Continue;
    }
}
