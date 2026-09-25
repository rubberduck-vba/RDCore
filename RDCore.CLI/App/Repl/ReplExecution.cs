using RDCore.SDK.Client;
using RDCore.SDK.ConsoleIO.Model;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.CLI.App.Repl;

/// <summary>
/// Runs a module through the language server and renders what came back — shared by <c>RUN</c> and
/// by an immediate-mode line, which differ only in which procedure they ask for.
/// </summary>
internal static class ReplExecution
{
    /// <summary>
    /// Runs <paramref name="entryPoint"/> of <paramref name="source"/> and writes its output, then
    /// whatever ended it.
    /// </summary>
    /// <param name="context">The live session to run against and write to.</param>
    /// <param name="source">The complete module source.</param>
    /// <param name="entryPoint">The parameterless procedure to invoke.</param>
    /// <param name="token">Cancelled by a break at the keyboard, which stops the running program.</param>
    public static async Task ExecuteAsync(ReplCommandContext context, string source, string entryPoint, CancellationToken token)
    {
        if (!context.Platform.Provides<SessionExecute>())
        {
            context.Console.WriteMessage(MessageKind.Error, Resources.Repl_NotAvailable,
                string.Format(Resources.Repl_NotAvailable_Verbose, nameof(SessionExecute)));
            return;
        }

        var result = await context.Platform.ExecuteAsync(source, ReplProgram.ModuleName, entryPoint, token);
        Render(context.Console, result);
    }

    private static void Render(IReplConsole console, ExecuteSessionResult result)
    {
        foreach (var line in result.Output)
        {
            console.WriteLine(line);
        }

        switch (result.Outcome)
        {
            // an End statement stops the program; there is nothing further to say about it, which is
            // also what BASIC says.
            case ExecutionOutcome.Completed:
            case ExecutionOutcome.Halted:
                break;

            case ExecutionOutcome.SyntaxError:
                console.WriteMessage(MessageKind.Error, Resources.Repl_SyntaxError,
                    string.Join("; ", result.Diagnostics));
                break;

            case ExecutionOutcome.RuntimeError:
                console.WriteMessage(MessageKind.Error,
                    string.Format(Resources.Repl_RuntimeError, result.ErrorMessage.ToUpperInvariant()),
                    $"{result.ErrorNumber}");
                break;

            case ExecutionOutcome.Interrupted:
                console.WriteLine(Resources.Repl_Break);
                break;

            case ExecutionOutcome.NotFound:
                console.WriteMessage(MessageKind.Error, Resources.Repl_NotFound, result.ErrorMessage);
                break;

            default:
                console.WriteMessage(MessageKind.Error, Resources.Repl_NotImplemented, result.ErrorMessage);
                break;
        }
    }
}
