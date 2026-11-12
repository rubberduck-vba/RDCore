using RDCore.CLI.App.Repl.Commands;
using RDCore.SDK.Client;
using RDCore.CLI.ConsoleIO.Model;
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
            await RenderAsync(context, await context.Platform.ExecuteAsync(source, context.Program.Module, entryPoint, token));
            return;
        }

        Report(context, await context.Debugger.PushBreakpointsAsync(context, token));

        // an error nothing handles waits where it was raised, as in the VBIDE, to be looked at and gone on with. The session has the mode until it is said
        // otherwise, but a host that was started again has not: it is said with every run, as the breakpoints are.
        await context.Platform.SetErrorBreakAsync(ErrorBreakMode.Unhandled, token);
        context.Debugger.Running();
        try
        {
            var result = await context.Platform.ExecuteAsync(source, context.Program.Module, entryPoint, debug: true, immediate: false, CancellationToken.None);
            await RenderAsync(context, result);
            if (result.Outcome is ExecutionOutcome.Refused)
            {
                context.Debugger.Idle();
            }
            else if (result.Outcome is not (ExecutionOutcome.SyntaxError or ExecutionOutcome.NotFound))
            {
                // the program was loaded as it is typed now, so the platform can say which lines a breakpoint can be on, and the others are dropped. A dropped
                // breakpoint of a workspace module was set on a line of a file, not of the program, so the shell says it.
                var dropped = await context.Debugger.PushBreakpointsAsync(context, CancellationToken.None, judge: true);
                Report(context, [.. dropped.Where(refusal => refusal.Module is not null)]);
            }
        }
        catch
        {
            context.Debugger.Idle();
            throw;
        }
    }

    // a breakpoint whose condition is refused is dropped before the program goes, and the person is told which, and why: BREAK refuses the condition as it is typed,
    // and this is the one a platform that did not debug then refuses now. So is one of a module whose line no statement begins on.
    private static void Report(ReplCommandContext context, IReadOnlyList<ReplBreakpointRefusal> refused)
    {
        foreach (var refusal in refused)
        {
            context.Console.WriteMessage(MessageKind.Error, refusal.Message);
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
        ExecuteResult result;
        try
        {
            result = await context.Platform.ResumeAsync(step, CancellationToken.None);
            if (result.Outcome is not ExecutionOutcome.Refused)
            {
                // where a STEP stops is said, and not listed: a listing is what a break prints.
                await RenderAsync(context, result, listsTheBreak: step is null);
                return;
            }
        }
        catch
        {
            context.Debugger.Idle();
            throw;
        }

        // a program that cannot be gone on with is still where it was, whatever saying why costs; and why is said: the edits that are not applied to it, when
        // they are why.
        context.Debugger.Suspended(waitedBefore);
        await RenderRefusalAsync(context, result.ErrorMessage, CancellationToken.None);
    }

    /// <summary>
    /// Says why the program that waits cannot be gone on with, stepped or moved: each edit made while it waited that is not applied to it, by its name and why,
    /// and that <c>RUN</c> starts it again; or what the platform said, when it is refused for anything else.
    /// </summary>
    /// <remarks>
    /// What is not applied is asked of the platform once, after the refusal: the reasons are its words, in the session's language, and are said as given. A
    /// platform that cannot say is taken at the refusal's word.
    /// <para>
    /// The edits are said in place of the refusal's own reason whenever there are any, which is right because the host refuses for them before anything else:
    /// a resume before it is resumed, and a goto before its line or label is looked for (<c>ProgramExecution.ResumeAsync</c>, <c>Goto</c>). A host that
    /// checked a label first would have its "no such label" said as the edits.
    /// </para>
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
    public static void WriteEdits(IReplConsole console, IEnumerable<DebugEdit> edits)
    {
        foreach (var edit in edits)
        {
            console.WriteLine("  " + string.Format(Resources.Repl_EditNamed, edit.Name, edit.Reason));
        }
    }

    // the edits the platform says are not applied, or nothing when it cannot say: a language server that has no such request (JSON-RPC -32601) is no reason to
    // say less of a refusal. Any other failure of the request is one, and is not hidden behind the refusal's words.
    private static async Task<DebugEditStatusResult?> EditStatusOrNoneAsync(ReplCommandContext context, CancellationToken token)
    {
        try
        {
            return await context.Platform.GetEditStatusAsync(token);
        }
        catch (OmniSharp.Extensions.JsonRpc.Server.MethodNotSupportedException)
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
            await context.Platform.DiscardAsync(context.Program.Module, endProgram: true, token);
        }
        else
        {
            await context.Platform.DiscardAsync(context.Program.Module, token);
        }

        context.Debugger.Idle();
    }

    /// <summary>
    /// Writes what a run printed, then what ended it - or what it waits at - as BASIC says it. A break first lists the lines around the one it stopped at, as
    /// many on either side as <see cref="ReplCommandContext.BreakListingLines"/> says, with that line marked as the statement the program waits before; a
    /// run-time error lists them the same way around the line of the program that raised it, before it is said. A program that waits at an error nothing handles
    /// is listed as a break is, and the error is said as a run it ended says it.
    /// </summary>
    /// <param name="context">The live session.</param>
    /// <param name="result">The answer to the request that ran the program, or went on with it.</param>
    /// <param name="listsTheBreak">Whether a break, or a run-time error, lists the lines around the one it stopped at.</param>
    public static async Task RenderAsync(ReplCommandContext context, ExecuteResult result, bool listsTheBreak = true)
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

            // the program waits where it stopped, and BREAK IN says where, as it does for a program that was stopped for good. A program that waits where an error
            // nothing handles was raised says the error instead, as a run it ended would.
            case ExecutionOutcome.Suspended:
                var waitsBefore = program.LineNumberAt(result.ErrorLine);
                var waitsAtAnError = result.ErrorNumber != 0;
                context.Debugger.Suspended(waitsBefore);

                // the lines around the stop come first, and what stopped it after them. A program that waits in a module of the workspace is listed, and
                // said, by that module's own lines.
                if (waitsBefore is null && await WaitsInAModuleAsync(context) is { } frame)
                {
                    if (listsTheBreak)
                    {
                        await ListAroundInModuleAsync(context, frame.Module, frame.Line);
                    }

                    if (waitsAtAnError)
                    {
                        RenderRuntimeError(console, program, result, inModule: (frame.Module, frame.Line + 1));
                    }
                    else
                    {
                        console.WriteLine(string.Format(Resources.Repl_Break_InModuleLine, frame.Module, frame.Line + 1));
                    }
                }
                else
                {
                    if (listsTheBreak)
                    {
                        await ListAroundAsync(context, waitsBefore);
                    }

                    if (waitsAtAnError)
                    {
                        RenderRuntimeError(console, program, result);
                    }
                    else
                    {
                        console.WriteLine(waitsBefore is { } waiting ? string.Format(Resources.Repl_Break_InLine, waiting) : Resources.Repl_Break);
                    }
                }

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

            // the lines around the statement that raised the error are listed as a break lists the lines around the one it stopped at, and the error is said
            // after them. An error raised in a module of the workspace is listed, and said, by that module's own lines.
            case ExecutionOutcome.RuntimeError:
                if (InAModuleOfTheWorkspace(program, result.ErrorModule))
                {
                    if (listsTheBreak)
                    {
                        await ListAroundInModuleAsync(context, result.ErrorModule, result.ErrorLine);
                    }

                    RenderRuntimeError(console, program, result, inModule: (result.ErrorModule, result.ErrorLine + 1));
                    break;
                }

                if (listsTheBreak)
                {
                    await ListAroundAsync(context, program.LineNumberAt(result.ErrorLine));
                }

                RenderRuntimeError(console, program, result);
                break;

            // BASIC has always said where it was stopped. A break the host could not place - one that arrived between two statements of nothing the
            // program wrote - is a break all the same.
            case ExecutionOutcome.Interrupted:
                var stoppedAt = program.LineNumberAt(result.ErrorLine);
                if (listsTheBreak)
                {
                    await ListAroundAsync(context, stoppedAt);
                }

                console.WriteLine(stoppedAt is { } line ? string.Format(Resources.Repl_Break_InLine, line) : Resources.Repl_Break);
                break;

            case ExecutionOutcome.NotFound:
                console.WriteMessage(MessageKind.Error, Resources.Repl_NotFound, result.ErrorMessage);
                break;

            default:
                console.WriteMessage(MessageKind.Error, Resources.Repl_NotImplemented, result.ErrorMessage);
                break;
        }
    }

    // the lines around the one a break stopped at, as LIST lists them, with that line marked as the statement the program waits before: as many lines of the program
    // above it and below it as there are, up to the setting. Every line of them is listed, a folded block's too: they are what the program was doing. A break that
    // could not be placed lists nothing. The requests are not the run's to cancel: a break at the keyboard cancelled the run's token already.
    private static async Task ListAroundAsync(ReplCommandContext context, int? stoppedAt)
    {
        var around = Math.Clamp(context.BreakListingLines, 0, AppOptions.MaximumBreakListingLines);
        if (around == 0 || stoppedAt is not { } stopped || context.Program.IndexOf(stopped) is not (>= 0 and var index))
        {
            return;
        }

        var from = context.Program.NumberAt(Math.Max(0, index - around));
        var to = context.Program.NumberAt(Math.Min(context.Program.Count - 1, index + around));
        await ListReplCommand.WriteAsync(context, from, to, current: stopped, withFolds: false, CancellationToken.None);
    }

    // the activation the program waits in, when it is in a module other than the shell's own: the shell's program is said by its line numbers, a module by its
    // lines. Nothing when the platform cannot say.
    private static async Task<DebugStackFrame?> WaitsInAModuleAsync(ReplCommandContext context)
    {
        if (!context.Platform.Provides<ProgramDebugging>()
            || (await context.Platform.GetStackAsync(CancellationToken.None))?.Frames.FirstOrDefault() is not { Line: >= 0 } innermost
            || string.Equals(innermost.Module, context.Program.Module, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return innermost;
    }

    // a module other than the shell's own: the shell's program is said by its line numbers, a module by its lines. A run that does not say its module is the
    // shell's.
    private static bool InAModuleOfTheWorkspace(ReplProgram program, string module)
        => module.Length > 0 && !string.Equals(module, program.Module, StringComparison.OrdinalIgnoreCase);

    // the lines around the one a break stopped at in a module of the workspace, as LIST lists the program, read from the module's file: as many above it and below
    // it as the setting says. A module the language server cannot find, or a file that cannot be read, lists nothing: the break is said all the same.
    private static async Task ListAroundInModuleAsync(ReplCommandContext context, string module, int line)
    {
        var around = Math.Clamp(context.BreakListingLines, 0, AppOptions.MaximumBreakListingLines);
        if (around == 0)
        {
            return;
        }

        Uri? document;
        string[] text;
        try
        {
            document = await context.Platform.FindModuleAsync(module, CancellationToken.None);
            if (document is not { IsFile: true })
            {
                return;
            }

            text = context.Files.File.ReadAllText(document.LocalPath).Split(["\r\n", "\n"], StringSplitOptions.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return;
        }

        await ListReplCommand.WriteModuleAsync(
            context, document, text, line - around, line + around, line, context.Debugger.ModuleBreakpoints.Of(module), CancellationToken.None);
    }

    // BASIC has always said which line it died on, and so does this: the title carries the program's own
    // line number, and the detail carries what a reader needs after that - the diagnostic code and message,
    // whose error it is, and the stack it was raised on.
    // An error raised in a module of the workspace is said by that module's line (inModule), the line of its file.
    private static void RenderRuntimeError(IReplConsole console, ReplProgram program, ExecuteResult result, (string Module, int Line)? inModule = null)
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
            inModule is { } at
                ? string.Format(Resources.Repl_RuntimeError_InModuleLine, title, at.Module, at.Line)
                : faultedLine is { } number
                ? string.Format(Resources.Repl_RuntimeError_InLine, title, number)
                : string.Format(Resources.Repl_RuntimeError, title),
            // the writer indents the verbose block as a whole, so every line after the first carries its own.
            string.Join($"{Environment.NewLine}  ", Detail(program, result)));
    }

    private static IEnumerable<string> Detail(ReplProgram program, ExecuteResult result)
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
            // An activation of a module of the workspace is at a line of that module's file, as STACK says it.
            var line = program.LineNumberAt(frame.Line);
            var at = InAModuleOfTheWorkspace(program, frame.Module) && frame.Line >= 0
                ? $"  {frame.Procedure} {string.Format(Resources.Repl_Frame_InModuleLine, frame.Module, frame.Line + 1)}"
                : line is { } number ? $"  {frame.Procedure} {number}" : $"  {frame.Procedure}";

            // and how the activation got there, when it was by a GoSub: from the line of the last one it had not returned from, and the one before that - as STACK
            // says it of a program that waits.
            yield return at + string.Concat(frame.ReturnLines.Select(gosub => program.LineNumberAt(gosub) is { } from ? $" from {from}" : string.Empty));
        }
    }
}
