namespace RDCore.SDK.Platform.Protocol;

/// <summary>
/// Request for <c>rdcore/session/execute</c>: a client asks the language server to run one procedure
/// of a module it supplies.
/// </summary>
/// <remarks>
/// The source travels with the request rather than being read from the workspace, because the code a
/// client wants to run is not always a file: an interactive shell's program lives in a buffer, and
/// its immediate-mode line is a procedure that exists for exactly one statement's worth of time. The
/// language server parses it, defines its symbols, and hands the result to whichever component owns
/// the runtime session.
/// <para>
/// Cancelling the request is how a caller interrupts a running program — the shell's
/// <kbd>Ctrl</kbd>+<kbd>Break</kbd>. The cancellation reaches the interpreter loop itself, so a
/// program that will never finish on its own still stops.
/// </para>
/// </remarks>
public record class SessionExecuteParams
{
    /// <summary>
    /// The complete source of the module to run — every procedure of it, not just the one being
    /// invoked, since the entry point may call the others.
    /// </summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>
    /// The module's programmatic name, which is the scope the entry point is resolved in.
    /// </summary>
    public string ModuleName { get; init; } = string.Empty;

    /// <summary>
    /// The name of the parameterless procedure to invoke.
    /// </summary>
    public string EntryPoint { get; init; } = string.Empty;

    /// <summary>
    /// Whether the program runs under a debugger: a <c>Stop</c>, a failed <c>Debug.Assert</c> or a break does not end the request but suspends the program, and the
    /// answer is <see cref="ExecutionOutcome.Suspended"/>. It is resumed, stepped or ended by the requests of <c>rdcore/session/debug/*</c>. Without it a
    /// <c>Stop</c> ends the run as <see cref="ExecutionOutcome.Interrupted"/>, as it always did.
    /// </summary>
    public bool Debug { get; init; }

    /// <summary>
    /// Whether the entry point is a statement typed at a prompt, to be run in the session as it is. While a program waits at a stop it is run alongside the program,
    /// which waits still - this is how its variables are read and set - and without it a run while a program waits starts the program over.
    /// </summary>
    public bool Immediate { get; init; }

    /// <summary>
    /// Whether the module is the client's own, of the runtime session and not of the project (<c>sessionModule</c> on the wire): a shell's program, which the project
    /// of a workspace the shell is attached to does not list. When the session has no module of that name, the module is defined as a standard module of the session
    /// at the workspace root, as the code of an Immediate window is, and the project file is not changed. It is then a module the session has: a later run finds it
    /// with or without this flag, until the project is reloaded and the session composed again.
    /// </summary>
    /// <remarks>
    /// Without it, a module the session does not have is not found (<see cref="ExecutionOutcome.NotFound"/>). A module of the client's own cannot be named after the
    /// project or a library it references: they and the project's procedural modules are matched in one tier (<strong>MS-VBAL §5.6.10</strong>), so the name would
    /// match twice, and every name qualified by it would be invalid. The run is not found, and the result says why.
    /// </remarks>
    public bool SessionModule { get; init; }
}

/// <summary>
/// The result of a run. The same result answers both hops — the language server passes the host's
/// answer back to the client unchanged, except for a parse that never reached the host at all.
/// </summary>
public record class ExecuteResult
{
    /// <summary>How the run ended.</summary>
    public ExecutionOutcome Outcome { get; init; }

    /// <summary>
    /// Everything the program printed, in order, one entry per line — including a line it left open
    /// with a trailing <c>;</c>.
    /// </summary>
    public IReadOnlyList<string> Output { get; init; } = [];

    /// <summary>
    /// How many lines the host has said by notification (<c>rdcore/host/output</c>) since it started, when this answer was given. A run that streams has
    /// said its lines before it answers, and they are not in <see cref="Output"/>; this is what lets the receiver wait for them.
    /// </summary>
    public long StreamedLines { get; init; }

    /// <summary>
    /// The syntax errors, when <see cref="Outcome"/> is <see cref="ExecutionOutcome.SyntaxError"/>.
    /// </summary>
    public IReadOnlyList<string> Diagnostics { get; init; } = [];

    /// <summary>
    /// The run-time error's number, when <see cref="Outcome"/> is
    /// <see cref="ExecutionOutcome.RuntimeError"/>; <c>0</c> otherwise.
    /// </summary>
    public int ErrorNumber { get; init; }

    /// <summary>
    /// The run-time error's description, or a short explanation of any other non-<see cref="ExecutionOutcome.Completed"/> outcome.
    /// </summary>
    public string ErrorMessage { get; init; } = string.Empty;

    /// <summary>
    /// The run-time error's <strong>RD-VBAL §2.6.3</strong> diagnostic code — <c>VBR</c> for one the
    /// runtime semantics reported, <c>VBA</c> for one the program raised itself with <c>Error</c> or
    /// <c>Err.Raise</c>. Empty for any other outcome.
    /// </summary>
    public string ErrorCode { get; init; } = string.Empty;

    /// <summary>
    /// The error's <em>category</em>, as a reader sees it in a title: which of <strong>RD-VBAL §2.6</strong>'s
    /// families raised it - "Run-time error" or "Application error" for a run, localized. What it *was* is
    /// <see cref="ErrorMessage"/>. Empty for any other outcome.
    /// </summary>
    public string ErrorTitle { get; init; } = string.Empty;

    /// <summary>
    /// What <c>Err.Source</c> reports: the object or application that generated the error, defaulting to
    /// the project's own name (<strong>MS-VBAL §6.1.3.2.2.6</strong>). Empty for any other outcome.
    /// </summary>
    public string ErrorSource { get; init; } = string.Empty;

    /// <summary>
    /// Where in the submitted <see cref="SessionExecuteParams.Source"/> the error was raised, zero-based
    /// as every position in the platform is: the line, and <see cref="ErrorCharacter"/> within it. Both
    /// <c>-1</c> when the outcome is not a run-time error, or when nothing located it.
    /// </summary>
    /// <remarks>
    /// A position in the source the <em>caller sent</em>, which is the one document both ends of the
    /// protocol agree on. A client whose program does not live in a file — an interactive shell's buffer —
    /// maps it back to whatever it calls a line itself.
    /// </remarks>
    public int ErrorLine { get; init; } = -1;

    /// <inheritdoc cref="ErrorLine"/>
    public int ErrorCharacter { get; init; } = -1;

    /// <summary>
    /// 🎯 The <em>line number</em> the error was raised at - what <c>Erl</c> reports: the nearest line-number
    /// label at or before the faulting statement, or <c>0</c> when none precedes it. Also <c>0</c> for any
    /// other outcome.
    /// </summary>
    public long ErrorLineNumber { get; init; }

    /// <summary>
    /// Why the condition of the breakpoint the program waits at could not be evaluated, when <see cref="Outcome"/> is <see cref="ExecutionOutcome.Suspended"/> and that
    /// is why it waits: a breakpoint whose condition raises an error is waited at, and this is what the person is told of it. Empty otherwise. The program does
    /// not wait on the error: it is the condition's and not the program's, and <see cref="ErrorNumber"/> stays <c>0</c>.
    /// </summary>
    public string ConditionError { get; init; } = string.Empty;

    /// <summary>
    /// 🎯 The call stack the error was raised on, innermost activation first - what <c>Err.StackTrace</c>
    /// reports. Empty for any other outcome.
    /// </summary>
    public IReadOnlyList<ExecuteStackFrame> StackTrace { get; init; } = [];
}

/// <summary>
/// One activation of the call stack a run-time error was raised on.
/// </summary>
/// <remarks>
/// Structured rather than pre-formatted, because a position only means something to the client: the
/// source both ends agree on is the one the client sent, and a client whose program does not live in a
/// file has its own idea of what a line is called. Formatting it here would force the shell to display a
/// line number its user never typed.
/// </remarks>
/// <param name="Procedure">The name of the procedure the activation is of.</param>
/// <param name="Line">
/// Zero-based line in the submitted source, or <c>-1</c> when the activation carries no location — true
/// of every activation but the one the error was raised in, since an activation record does not say
/// where in itself it is suspended.
/// </param>
/// <param name="Character">Zero-based character within <paramref name="Line"/>, or <c>-1</c>.</param>
/// <param name="ReturnLines">
/// The zero-based lines of the <c>GoSub</c> statements the activation had not <c>Return</c>ed from when the error was raised, innermost first - how it got to where it
/// was, as the stack of a program that waits says it. Empty for an activation in no <c>GoSub</c>.
/// </param>
public record class ExecuteStackFrame(string Procedure, int Line = -1, int Character = -1, IReadOnlyList<int>? ReturnLines = null)
{
    /// <summary>The lines of the <c>GoSub</c> statements the activation would have returned to; none for one that was in none.</summary>
    public IReadOnlyList<int> ReturnLines { get; init; } = ReturnLines ?? [];
}
