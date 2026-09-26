using MediatR;
using OmniSharp.Extensions.JsonRpc;
using RDCore.SDK.Client;
using RDCore.SDK.Model.AST;

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
[Method(RDCorePlatformProtocol.SessionExecute, Direction.ClientToServer)]
public record class ExecuteSessionParams : IRequest, IRequest<ExecuteSessionResult>
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
}

/// <summary>
/// Request for <c>rdcore/host/execute</c>: the language-server side of
/// <see cref="ExecuteSessionParams"/>, addressed to the component that owns the runtime session.
/// </summary>
/// <remarks>
/// The parsed module rides a <see cref="System.Text.Json"/> string for the same reason
/// <see cref="DiagnoseDocumentRequest"/>'s does — the AST is polymorphic and the JSON-RPC
/// transport's own serializer cannot round-trip it (see <see cref="PlatformJson"/>). The language
/// server has already parsed and defined the module's symbols by the time it sends this, so the host
/// lowers and runs rather than re-deriving any of it.
/// </remarks>
[Method(RDCorePlatformProtocol.HostExecute, Direction.ClientToServer)]
public record class HostExecuteParams : IRequest, IRequest<ExecuteSessionResult>
{
    /// <summary>
    /// The <see cref="System.Text.Json"/> representation of a <see cref="HostExecutePayload"/>.
    /// </summary>
    public string Json { get; init; } = string.Empty;

    /// <summary>
    /// The module's programmatic name — the scope <see cref="EntryPoint"/> is resolved in.
    /// </summary>
    public string ModuleName { get; init; } = string.Empty;

    /// <summary>
    /// The name of the parameterless procedure to invoke.
    /// </summary>
    public string EntryPoint { get; init; } = string.Empty;
}

/// <summary>
/// The <see cref="System.Text.Json"/> payload carried in <see cref="HostExecuteParams.Json"/>.
/// </summary>
/// <param name="DocumentUri">The document the module was parsed from.</param>
/// <param name="ParseResult">The parsed module — AST plus syntax errors.</param>
public record class HostExecutePayload(Uri DocumentUri, ModuleParseResult ParseResult);

/// <summary>
/// How a run ended.
/// </summary>
public enum ExecutionOutcome
{
    /// <summary>The entry point ran to completion.</summary>
    Completed,

    /// <summary>The source did not parse; <see cref="ExecuteSessionResult.Diagnostics"/> says why.</summary>
    SyntaxError,

    /// <summary>There is no such procedure to invoke, or no runtime session to invoke it in.</summary>
    NotFound,

    /// <summary>A run-time error was raised and nothing handled it.</summary>
    RuntimeError,

    /// <summary>An <c>End</c> statement stopped the program.</summary>
    Halted,

    /// <summary>A <c>Stop</c> statement, or the caller cancelling the request, interrupted the program.</summary>
    Interrupted,

    /// <summary>
    /// The interpreter reached something it has no implementation for. Not a program error: a gap.
    /// </summary>
    NotImplemented,
}

/// <summary>
/// The result of a run. The same result answers both hops — the language server passes the host's
/// answer back to the client unchanged, except for a parse that never reached the host at all.
/// </summary>
public record class ExecuteSessionResult
{
    /// <summary>How the run ended.</summary>
    public ExecutionOutcome Outcome { get; init; }

    /// <summary>
    /// Everything the program printed, in order, one entry per line — including a line it left open
    /// with a trailing <c>;</c>.
    /// </summary>
    public IReadOnlyList<string> Output { get; init; } = [];

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
    /// Where in the submitted <see cref="ExecuteSessionParams.Source"/> the error was raised, zero-based
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
public record class ExecuteStackFrame(string Procedure, int Line = -1, int Character = -1);
