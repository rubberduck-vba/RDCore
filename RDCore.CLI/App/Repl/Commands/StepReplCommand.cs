using RDCore.SDK.Client;
using RDCore.SDK.ConsoleIO.Model;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.CLI.App.Repl.Commands;

/// <summary>
/// <c>STEP</c>: runs one statement of the program that was stopped, and stops again.
/// </summary>
/// <remarks>
/// <c>STEP</c> runs a call as one statement, <c>STEP INTO</c> goes into it, and <c>STEP OUT</c> runs on until the procedure that was stepped into returns.
/// </remarks>
internal sealed class StepReplCommand : IReplCommand
{
    public string Name => ReplCommandNames.Step;
    public IReadOnlyList<string> Aliases => [];
    public string Summary => Resources.Repl_Step_Summary;

    public async Task<ReplCommandResult> ExecuteAsync(ReplCommandContext context, string arguments, CancellationToken token)
    {
        if (context.Debugger.State is not ReplDebugState.Suspended || !context.Platform.Provides<ProgramDebugging>())
        {
            context.Console.WriteMessage(MessageKind.Error, Resources.Repl_CantContinue);
            return ReplCommandResult.Continue;
        }

        if (!TryParse(arguments, out var step))
        {
            context.Console.WriteMessage(MessageKind.Error, Resources.Repl_SyntaxError, arguments);
            return ReplCommandResult.Continue;
        }

        await ReplExecution.ResumeAsync(context, step);
        return ReplCommandResult.Continue;
    }

    /// <summary>
    /// Parses the argument of <c>STEP</c>.
    /// </summary>
    /// <param name="arguments">Nothing, or <c>INTO</c>, <c>OVER</c> or <c>OUT</c>.</param>
    /// <param name="step">How far to step; <see cref="StepKind.Over"/> when nothing is said.</param>
    /// <returns><c>false</c> if the argument is none of them.</returns>
    internal static bool TryParse(string arguments, out StepKind step)
    {
        switch (arguments.Trim().ToUpperInvariant())
        {
            case "" or "OVER":
                step = StepKind.Over;
                return true;

            case "INTO":
                step = StepKind.Into;
                return true;

            case "OUT":
                step = StepKind.Out;
                return true;

            default:
                step = StepKind.Over;
                return false;
        }
    }
}
