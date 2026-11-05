using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.SDK.Platform.Protocol;

/// <summary>
/// Request for <c>rdcore/session/debug/resume</c>: a client asks the language server to go on with the program it ran under a debugger
/// (<see cref="SessionExecuteParams.Debug"/>) that waits where it stopped, and is answered an <see cref="ExecuteResult"/> as of the next time the program
/// waits or is over.
/// </summary>
public record class SessionDebugResumeParams
{
    /// <summary>
    /// How far the program goes before it waits again, or <see langword="null"/> for as far as it goes: to the next stop, or to its end.
    /// </summary>
    public StepKind? Step { get; init; }
}

/// <summary>
/// Request for <c>rdcore/session/debug/pause</c>: stops the program that is running, at the next instruction, so that it waits there; answered a
/// <see cref="DebugAck"/>. The request that is running the program answers <see cref="ExecutionOutcome.Suspended"/>.
/// </summary>
/// <remarks>
/// This, and not cancelling the request that runs the program, is what a break at the keyboard is for a program under a debugger: a cancelled request has no answer to
/// read the place the program stopped at from.
/// </remarks>
public record class SessionDebugPauseParams;

/// <summary>
/// Request for <c>rdcore/session/debug/goto</c>: moves the point the program that waits goes on from (<c>Set Next Statement</c>), within the procedure it waits in;
/// answered a <see cref="DebugGotoResult"/>. Nothing runs.
/// </summary>
public record class SessionDebugGotoParams
{
    /// <summary>
    /// The zero-based line of the source to go on from: the first statement that begins on it, or after it. Ignored when <see cref="Label"/> is given.
    /// </summary>
    public int Line { get; init; }

    /// <summary>
    /// A statement label or line number of the procedure to go on from (<c>100</c>, <c>Retry</c>), as a shell that numbers its lines says it. Takes the place of
    /// <see cref="Line"/>.
    /// </summary>
    public string? Label { get; init; }
}

/// <summary>
/// Request for <c>rdcore/session/debug/editStatus</c>: which edits made while a program waits are not applied to it, and why; answered a
/// <see cref="DebugEditStatusResult"/>.
/// </summary>
public record class SessionDebugEditStatusParams;

/// <summary>
/// Request for <c>rdcore/session/debug/terminate</c>: ends the program that is running or waits; answered a <see cref="DebugAck"/>. Its variables stay, as they do
/// when a program is stopped, unless <see cref="Wipe"/> says they go.
/// </summary>
public record class SessionDebugTerminateParams
{
    /// <summary>
    /// Whether the session is wiped, as an <c>End</c> wipes it, once the program is ended: <c>NEW</c> and <c>LOAD</c> do, a restart does not.
    /// </summary>
    public bool Wipe { get; init; }
}

/// <summary>
/// Request for <c>rdcore/session/debug/breakpoints</c>: sets the lines of a module that a program under a debugger waits at, replacing the breakpoints it had;
/// answered a <see cref="DebugBreakpointsResult"/>.
/// </summary>
public record class SessionDebugBreakpointsParams
{
    /// <summary>The programmatic name of the module.</summary>
    public string ModuleName { get; init; } = string.Empty;

    /// <summary>The breakpoints, one per line of the source. None removes the module's breakpoints.</summary>
    public IReadOnlyList<SessionBreakpointLine> Breakpoints { get; init; } = [];
}

/// <summary>
/// A breakpoint as a client asks for it: a line, whether it is on, and the condition it waits on, as the person typed it.
/// </summary>
/// <remarks>
/// The language server parses the condition. A condition that is not an expression is refused there, and the result says why (<see cref="DebugBreakpoint.ConditionError"/>);
/// it is not evaluated until the program reaches the line, since what its names are is the activation's that reaches it.
/// </remarks>
public record class SessionBreakpointLine
{
    /// <summary>The zero-based line of the source.</summary>
    public int Line { get; init; }

    /// <summary>Whether the program waits at the line. A disabled breakpoint is kept, and is not waited at.</summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>
    /// The source of the expression the program waits on when it reaches the line, a <c>Boolean</c> condition as an <c>If</c> has; <see langword="null"/> or blank for
    /// a breakpoint that is always waited at. A condition that raises an error is waited at, and the person is told why (<see cref="ExecuteResult.ConditionError"/>).
    /// </summary>
    public string? Condition { get; init; }
}

/// <summary>
/// Request for <c>rdcore/session/debug/stack</c>: the activations of the program that waits, innermost first; answered a <see cref="DebugStackResult"/>.
/// </summary>
public record class SessionDebugStackParams;

/// <summary>
/// Request for <c>rdcore/session/debug/variables</c>: the variables of an activation of the program that waits, or the parts of one of them; answered a
/// <see cref="DebugVariablesResult"/>.
/// </summary>
public record class SessionDebugVariablesParams
{
    /// <summary>The activation, by <see cref="DebugStackFrame.Id"/>. Ignored when <see cref="Reference"/> is given.</summary>
    public int FrameId { get; init; }

    /// <summary>Which variables of the activation.</summary>
    public DebugVariableScope Scope { get; init; }

    /// <summary>
    /// The <see cref="DebugVariable.Reference"/> of a variable that has parts - the elements of an array, the fields of a user-defined type - to get them instead.
    /// <c>0</c> for the variables of <see cref="Scope"/>.
    /// </summary>
    public int Reference { get; init; }
}

/// <summary>
/// Request for <c>rdcore/session/debug/evaluate</c>: the value of an expression, written as text, in an activation of the program that waits; answered a
/// <see cref="DebugEvaluateResult"/>. The language server parses it and has the host evaluate it.
/// </summary>
/// <remarks>
/// Its names are looked up from the procedure of the activation - its locals and parameters, then its module, then the project - and a call in it is made, in the
/// session as it is: what the expression does stays done, as it does in the immediate window of the VBA editor.
/// </remarks>
public record class SessionDebugEvaluateParams
{
    /// <summary>The activation, by its <see cref="DebugStackFrame.Id"/>.</summary>
    public int FrameId { get; init; }

    /// <summary>The expression, as it would be written in the procedure of the activation.</summary>
    public string Expression { get; init; } = string.Empty;
}

/// <summary>
/// Request for <c>rdcore/session/debug/execute</c>: runs a statement, written as text, in an activation of the program that waits - the answer to a line typed in the
/// immediate window of a procedure that is stopped; answered a <see cref="DebugEvaluateResult"/> with no value. The language server parses it and has the host
/// run it.
/// </summary>
/// <remarks>
/// The statement's names are the activation's: a local of the procedure is the local the program goes on with, and a name the procedure does not see is not defined
/// - it declares nothing, since a statement typed at a stop is not part of the program's code. <c>?</c> is the immediate window's <c>Debug.Print</c>.
/// </remarks>
public record class SessionDebugExecuteParams
{
    /// <inheritdoc cref="SessionDebugEvaluateParams.FrameId"/>
    public int FrameId { get; init; }

    /// <summary>The statement, as it would be written in the procedure of the activation; one line.</summary>
    public string Statement { get; init; } = string.Empty;
}
