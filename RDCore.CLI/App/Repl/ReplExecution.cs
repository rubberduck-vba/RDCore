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
    /// <param name="statement">
    /// The statement typed at the prompt, when the entry point is one. While a program waits it is run in the activation <c>FRAME</c> selected and the program waits
    /// still, which is how its variables are read and set at a stop; <paramref name="source"/> is then not sent, since the program's code is not to be loaded again
    /// under it.
    /// </param>
    public static async Task ExecuteAsync(
        ReplCommandContext context, string source, string entryPoint, CancellationToken token, bool debug = false, string? statement = null)
    {
        if (!context.Platform.Provides<SessionExecute>())
        {
            context.Console.WriteMessage(MessageKind.Error, Resources.Repl_NotAvailable,
                string.Format(Resources.Repl_NotAvailable_Verbose, nameof(SessionExecute)));
            return;
        }

        if (statement is not null && context.Debugger.State is ReplDebugState.Suspended && context.Platform.Provides<ProgramDebugging>())
        {
            await ExecuteInFrameAsync(context, statement, token);
            return;
        }

        debug &= context.Platform.Provides<ProgramDebugging>();
        if (!debug)
        {
            Render(context, await context.Platform.ExecuteAsync(source, ReplProgram.ModuleName, entryPoint, token));
            return;
        }

        Report(context, await context.Debugger.PushBreakpointsAsync(context, token));
        context.Debugger.Running();
        try
        {
            var result = await context.Platform.ExecuteAsync(source, ReplProgram.ModuleName, entryPoint, debug: true, immediate: false, CancellationToken.None);
            Render(context, result);
            if (result.Outcome is ExecutionOutcome.Refused)
            {
                context.Debugger.Idle();
            }
            else if (result.Outcome is not (ExecutionOutcome.SyntaxError or ExecutionOutcome.NotFound))
            {
                // the program was loaded as it is typed now, so the platform can say which lines a breakpoint can be on, and the others are dropped.
                await context.Debugger.PushBreakpointsAsync(context, CancellationToken.None, judge: true);
            }
        }
        catch
        {
            context.Debugger.Idle();
            throw;
        }
    }

    // a breakpoint whose condition is refused is dropped before the program goes, and the person is told which, and why: BREAK refuses the condition as it is typed,
    // and this is the one a platform that did not debug then refuses now.
    private static void Report(ReplCommandContext context, IReadOnlyList<(Breakpoint Breakpoint, string Reason)> refused)
    {
        foreach (var (breakpoint, reason) in refused)
        {
            context.Console.WriteMessage(MessageKind.Error, string.Format(Resources.Repl_Breakpoint_ConditionRefused, breakpoint.LineNumber, reason));
        }
    }

    // A statement typed while a program waits runs in the activation FRAME selected, as if it were written in that procedure: its names are the procedure's, so a
    // local is read and set as the program has it, and a name the procedure does not see is not defined - the statement is not the program's code, and declares
    // nothing in it. The program waits still, unless the statement was an END, which is the end of it. What the statement printed, or why it could not run, is said.
    private static async Task ExecuteInFrameAsync(ReplCommandContext context, string statement, CancellationToken token)
    {
        var waitsBefore = context.Debugger.StoppedAt;
        var result = await context.Platform.ExecuteInFrameAsync(context.Debugger.SelectedFrame, statement, token);
        foreach (var line in result.Output)
        {
            context.Console.WriteLine(line);
        }

        if (result.Error is { } error)
        {
            context.Console.WriteMessage(MessageKind.Error, error);
        }

        if (result.EndedTheProgram)
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
        Report(context, await context.Debugger.PushBreakpointsAsync(context, CancellationToken.None));
        var waitedBefore = context.Debugger.StoppedAt;
        context.Debugger.Running();
        try
        {
            var result = await context.Platform.ResumeAsync(step, CancellationToken.None);
            if (result.Outcome is ExecutionOutcome.Refused)
            {
                // a program that cannot be gone on with is still where it was, and why is said: the edits that are not applied to it, when they are why.
                context.Debugger.Suspended(waitedBefore);
                await RenderRefusalAsync(context, result.ErrorMessage, CancellationToken.None);
                return;
            }

            Render(context, result);
        }
        catch
        {
            context.Debugger.Idle();
            throw;
        }
    }

    /// <summary>
    /// Says why the program that waits cannot be gone on with, stepped or moved: each edit made while it waited that is not applied to it, by its name and why,
    /// and that <c>RUN</c> starts it again; or what the platform said, when it is refused for anything else.
    /// </summary>
    /// <remarks>
    /// What is not applied is asked of the platform once, after the refusal: the reasons are its words, in the session's language, and are said as given. A
    /// platform that cannot say is taken at the refusal's word.
    /// </remarks>
    /// <param name="context">The live session.</param>
    /// <param name="reason">What the platform said the refusal was for.</param>
    /// <param name="token">A token that cancels the request.</param>
    public static async Task RenderRefusalAsync(ReplCommandContext context, string reason, CancellationToken token)
    {
        var console = context.Console;
        if (await EditStatusOrNoneAsync(context, token) is not { Edits.IsDefaultOrEmpty: false } status)
        {
            console.WriteMessage(MessageKind.Error, Resources.Repl_CantContinue, reason);
            return;
        }

        console.WriteMessage(MessageKind.Error, Resources.Repl_CantContinue, Resources.Repl_EditsNotApplied);
        WriteEdits(console, status.Edits);
        console.WriteLine(Resources.Repl_RunToStartAgain);
    }

    /// <summary>
    /// Writes each edit made while the program waits that is not applied to it: its name and why, as the platform says them.
    /// </summary>
    /// <param name="console">Where they are written.</param>
    /// <param name="edits">The edits.</param>
    public static void WriteEdits(IReplConsole console, IEnumerable<HostDebugEdit> edits)
    {
        foreach (var edit in edits)
        {
            console.WriteLine($"  {edit.Name}: {edit.Reason}");
        }
    }

    // the edits the platform says are not applied, or nothing when it cannot say: a language server that has no such request is no reason to say less of a refusal.
    private static async Task<HostDebugEditStatusResult?> EditStatusOrNoneAsync(ReplCommandContext context, CancellationToken token)
    {
        try
        {
            return await context.Platform.GetEditStatusAsync(token);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
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

                // a breakpoint whose condition could not be evaluated is waited at, and what the condition raised is said with it.
                if (result.ConditionError.Length > 0)
                {
                    console.WriteMessage(MessageKind.Warning, string.Format(Resources.Repl_Breakpoint_ConditionFailed, result.ConditionError));
                }
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
            var at = line is { } number ? $"  {frame.Procedure} {number}" : $"  {frame.Procedure}";

            // and how the activation got there, when it was by a GoSub: from the line of the last one it had not returned from, and the one before that - as STACK
            // says it of a program that waits.
            yield return at + string.Concat(frame.ReturnLines.Select(gosub => program.LineNumberAt(gosub) is { } from ? $" from {from}" : string.Empty));
        }
    }
}
