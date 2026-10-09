using RDCore.SDK.Client;
using RDCore.SDK.ConsoleIO.Model;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;

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
    /// <param name="debug">
    /// Whether the program runs under a debugger, when the platform has one: a <c>STOP</c> or a breakpoint then leaves it waiting, to be gone on with by <c>CONT</c>
    /// or <c>STEP</c>. A break at the keyboard pauses it then, instead of cancelling the request that runs it.
    /// </param>
    /// <param name="immediate">
    /// Whether the entry point is a statement typed at the prompt. While a program waits it is run alongside it and the program waits still, which is how its
    /// variables are read and set at a stop.
    /// </param>
    public static async Task ExecuteAsync(
        ReplCommandContext context, string source, string entryPoint, CancellationToken token, bool debug = false, bool immediate = false)
    {
        if (!context.Platform.Provides<SessionExecute>())
        {
            context.Console.WriteMessage(MessageKind.Error, Resources.Repl_NotAvailable,
                string.Format(Resources.Repl_NotAvailable_Verbose, nameof(SessionExecute)));
            return;
        }

        if (immediate && context.Debugger.State is ReplDebugState.Suspended && context.Platform.Provides<ProgramDebugging>())
        {
            await ExecuteAlongsideAsync(context, source, entryPoint, token);
            return;
        }

        debug &= context.Platform.Provides<ProgramDebugging>();
        if (!debug)
        {
            Render(context, await context.Platform.ExecuteAsync(source, ReplProgram.ModuleName, entryPoint, token));
            return;
        }

        await context.Debugger.PushBreakpointsAsync(context, token);
        context.Debugger.Running();
        try
        {
            var result = await context.Platform.ExecuteAsync(source, ReplProgram.ModuleName, entryPoint, debug: true, immediate: false, CancellationToken.None);
            Render(context, result);
            if (result.Outcome is ExecutionOutcome.Refused)
            {
                context.Debugger.Idle();
            }
        }
        catch
        {
            context.Debugger.Idle();
            throw;
        }
    }

    // A statement typed while a program waits runs in the session as the program left it, and the program waits still - unless the statement was an END, which is
    // the end of the program. What the statement printed, or what stopped it, is said as it is for any run, and is no business of what the program is doing.
    private static async Task ExecuteAlongsideAsync(ReplCommandContext context, string source, string entryPoint, CancellationToken token)
    {
        var waitsBefore = context.Debugger.StoppedAt;
        var result = await context.Platform.ExecuteAsync(source, ReplProgram.ModuleName, entryPoint, debug: false, immediate: true, token);
        Render(context, result);

        if (result.Outcome is ExecutionOutcome.Halted)
        {
            context.Debugger.Idle();
        }
        else
        {
            context.Debugger.Suspended(waitsBefore);
        }
    }

    /// <summary>
    /// Goes on with the program that waits, to the next place it waits or to its end, or for one step, and writes what it printed and what stopped it.
    /// </summary>
    /// <param name="context">The live session.</param>
    /// <param name="step">How far the program goes, or <see langword="null"/> for as far as it goes.</param>
    public static async Task ResumeAsync(ReplCommandContext context, StepKind? step)
    {
        await context.Debugger.PushBreakpointsAsync(context, CancellationToken.None);
        var waitedBefore = context.Debugger.StoppedAt;
        context.Debugger.Running();
        try
        {
            var result = await context.Platform.ResumeAsync(step, CancellationToken.None);
            Render(context, result);
            if (result.Outcome is ExecutionOutcome.Refused)
            {
                // a program that cannot be gone on with is still where it was.
                context.Debugger.Suspended(waitedBefore);
            }
        }
        catch
        {
            context.Debugger.Idle();
            throw;
        }
    }

    /// <summary>
    /// Takes the program out of the runtime session: its variables, the storage they were given, and its code.
    /// </summary>
    /// <remarks>
    /// Running a program defines its module in the session, and defining it again never removes what it no longer declares, so a variable a
    /// program made outlives the program - and the program that replaces it can read it. BASIC clears them with the program (<c>NEW</c>), when
    /// another is loaded (<c>LOAD</c>) and when it is run (<c>RUN</c>); a line typed at the prompt does none of this, so what an earlier line assigned is
    /// still there for the next. A platform that cannot discard a module leaves things as they were.
    /// </remarks>
    /// <param name="context">The live session.</param>
    /// <param name="token">Cancelled by a break at the keyboard.</param>
    public static async Task DiscardProgramAsync(ReplCommandContext context, CancellationToken token)
    {
        if (!context.Platform.Provides<SessionDiscard>())
        {
            return;
        }

        // a program that waits is ended with the program it is: clearing the program is what a person does to be rid of it.
        if (context.Platform.Provides<ProgramDebugging>())
        {
            await context.Platform.DiscardAsync(ReplProgram.ModuleName, endProgram: true, token);
        }
        else
        {
            await context.Platform.DiscardAsync(ReplProgram.ModuleName, token);
        }

        context.Debugger.Idle();
    }

    /// <summary>
    /// Writes what a run printed, then what ended it - or what it waits at - as BASIC says it.
    /// </summary>
    /// <param name="context">The live session.</param>
    /// <param name="result">The answer to the request that ran the program, or went on with it.</param>
    public static void Render(ReplCommandContext context, ExecuteSessionResult result)
    {
        var (console, program) = (context.Console, context.Program);
        foreach (var line in result.Output)
        {
            console.WriteLine(line);
        }

        // a program that is over is not running, and one that waits is neither: what the shell knows of it follows what the platform answered.
        if (result.Outcome is not (ExecutionOutcome.Suspended or ExecutionOutcome.Refused))
        {
            context.Debugger.Idle();
        }

        switch (result.Outcome)
        {
            // an End statement stops the program; there is nothing further to say about it, which is
            // also what BASIC says.
            case ExecutionOutcome.Completed:
            case ExecutionOutcome.Halted:
                break;

            // the program waits where it stopped, and BREAK IN says where, as it does for a program that was stopped for good.
            case ExecutionOutcome.Suspended:
                var waitsBefore = program.LineNumberAt(result.ErrorLine);
                context.Debugger.Suspended(waitsBefore);
                console.WriteLine(waitsBefore is { } waiting ? string.Format(Resources.Repl_Break_InLine, waiting) : Resources.Repl_Break);
                break;

            // it cannot go on, and what it was before is what it is: the caller says which.
            case ExecutionOutcome.Refused:
                console.WriteMessage(MessageKind.Error, Resources.Repl_CantContinue, result.ErrorMessage);
                break;

            case ExecutionOutcome.SyntaxError:
                console.WriteMessage(MessageKind.Error, Resources.Repl_SyntaxError,
                    string.Join("; ", result.Diagnostics));
                break;

            case ExecutionOutcome.RuntimeError:
                RenderRuntimeError(console, program, result);
                break;

            // BASIC has always said where it was stopped. A break the host could not place - one that arrived between two statements of nothing the
            // program wrote - is a break all the same.
            case ExecutionOutcome.Interrupted:
                console.WriteLine(program.LineNumberAt(result.ErrorLine) is { } stoppedAt
                    ? string.Format(Resources.Repl_Break_InLine, stoppedAt)
                    : Resources.Repl_Break);
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
