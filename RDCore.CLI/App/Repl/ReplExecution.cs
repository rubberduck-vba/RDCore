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
        Render(context.Console, context.Program, result);
    }

    private static void Render(IReplConsole console, ReplProgram program, ExecuteSessionResult result)
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
                RenderRuntimeError(console, program, result);
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

    // BASIC has always said which line it died on, and so does this: the title carries the program's own
    // line number, and the detail carries what a reader needs after that - the diagnostic code and message,
    // whose error it is, and the stack it was raised on.
    private static void RenderRuntimeError(IReplConsole console, ReplProgram program, ExecuteSessionResult result)
    {
        // the title is the error's *category* - what kind of thing went wrong - and the detail says what it
        // was. Uppercased because that is the shell's voice, not because the title is.
        var title = result.ErrorTitle.ToUpperInvariant();

        // the program's own line number, mapped from the position in the source that was submitted - not
        // Erl's, which counts lines of that generated source and would name a line nobody typed. (Erl
        // agrees when an environment is configured for MS-VBA's line-label counting, since every line here
        // carries a number; it is the mapping that is right either way.)
        var faultedLine = program.LineNumberAt(result.ErrorLine);

        console.WriteMessage(MessageKind.Error,
            faultedLine is { } number
                ? string.Format(Resources.Repl_RuntimeError_InLine, title, number)
                : string.Format(Resources.Repl_RuntimeError, title),
            // the writer indents the verbose block as a whole, so every line after the first carries its own.
            string.Join($"{Environment.NewLine}  ", Detail(program, result)));
    }

    private static IEnumerable<string> Detail(ReplProgram program, ExecuteSessionResult result)
    {
        yield return $"{result.ErrorCode}  {result.ErrorMessage}";

        if (result.ErrorSource is { Length: > 0 } source)
        {
            yield return string.Format(Resources.Repl_RuntimeError_Source, source);
        }

        if (result.StackTrace.Count == 0)
        {
            yield break;
        }

        yield return Resources.Repl_RuntimeError_StackTrace;
        foreach (var frame in result.StackTrace)
        {
            // a frame carries a source position rather than a line number - only the faulting statement's own
            // line number is captured (Erl is one value, not one per activation) - so the buffer maps it back.
            // TODO carry a line number per activation once a frame knows its own instruction list.
            var line = program.LineNumberAt(frame.Line);
            yield return line is { } number ? $"  {frame.Procedure} {number}" : $"  {frame.Procedure}";
        }
    }
}
