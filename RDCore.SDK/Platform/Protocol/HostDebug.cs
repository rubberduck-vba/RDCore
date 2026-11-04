using MediatR;
using OmniSharp.Extensions.JsonRpc;
using RDCore.SDK.Client;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Server;

namespace RDCore.SDK.Platform.Protocol;

/// <summary>
/// Request for <c>rdcore/host/debug/resume</c>: goes on with a program that was run under a debugger and waits where it stopped.
/// </summary>
/// <remarks>
/// The answer is that of the run (<see cref="ExecuteSessionResult"/>), as of the next time the program waits or is over: the request is the program running,
/// as <c>rdcore/host/execute</c> was. What the program printed meanwhile is in the answer, and not what it printed before.
/// <para>
/// A program that was changed while it waited cannot be resumed from where it is: the code it runs is the code it was suspended with, and resuming it with
/// something else underneath would run a program nobody wrote. The answer is then <see cref="ExecutionOutcome.Refused"/>, and the program still waits - it can be
/// run again, which picks the changes up, or the changes undone.
/// </para>
/// </remarks>
[Method(RDCorePlatformProtocol.HostDebugResume, Direction.ClientToServer)]
public record class HostDebugResumeParams : IRequest, IRequest<ExecuteSessionResult>
{
    /// <summary>
    /// How far the program goes before it waits again, or <see langword="null"/> for as far as it goes: to the next stop, or to its end.
    /// </summary>
    public StepKind? Step { get; init; }
}

/// <summary>
/// Request for <c>rdcore/host/debug/pause</c>: stops a program that is running, at the next instruction, so that it waits there. The request that is running
/// the program answers <see cref="ExecutionOutcome.Suspended"/>.
/// </summary>
/// <remarks>Handled as it arrives, while the request that runs the program runs (<see cref="HandledOnArrivalAttribute"/>).</remarks>
[HandledOnArrival]
[Method(RDCorePlatformProtocol.HostDebugPause, Direction.ClientToServer)]
public record class HostDebugPauseParams : IRequest, IRequest<HostDebugAck>;

/// <summary>
/// Request for <c>rdcore/host/debug/terminate</c>: ends a program that is running or waits. Its variables stay, as they do when a program is stopped, unless
/// <see cref="Wipe"/> says they go.
/// </summary>
/// <remarks>
/// Handled as it arrives, while the request that runs the program runs (<see cref="HandledOnArrivalAttribute"/>): a program that is running is stopped at its
/// next instruction, and ended by the request that runs it, which this answers after.
/// </remarks>
[HandledOnArrival]
[Method(RDCorePlatformProtocol.HostDebugTerminate, Direction.ClientToServer)]
public record class HostDebugTerminateParams : IRequest, IRequest<HostDebugAck>
{
    /// <summary>
    /// Whether the session is wiped, as an <c>End</c> wipes it, once the program is ended: <c>NEW</c> and <c>LOAD</c> do, a restart does not.
    /// </summary>
    public bool Wipe { get; init; }
}

/// <summary>
/// Request for <c>rdcore/host/debug/errorBreak</c>: says which run-time errors a program under a debugger waits at, where they are raised - before any handler has
/// caught them or anything has been unwound.
/// </summary>
/// <remarks>
/// A program that waits at an error is answered as one that waits at a <c>Stop</c> (<see cref="ExecutionOutcome.Suspended"/>), with the error in
/// <see cref="ExecuteSessionResult.ErrorNumber"/>, <see cref="ExecuteSessionResult.ErrorMessage"/>, <see cref="ExecuteSessionResult.ErrorCode"/> and
/// <see cref="ExecuteSessionResult.ErrorTitle"/>. Resumed, it deals with the error as it would have.
/// <para>Handled as it arrives, while a request that runs a program runs (<see cref="HandledOnArrivalAttribute"/>): the next error raised is weighed by it.</para>
/// </remarks>
[HandledOnArrival]
[Method(RDCorePlatformProtocol.HostDebugErrorBreak, Direction.ClientToServer)]
public record class HostDebugErrorBreakParams : IRequest, IRequest<HostDebugAck>
{
    /// <summary>Which errors the program waits at. It is the session's, and it stays until it is said otherwise.</summary>
    public ErrorBreakMode Mode { get; init; }
}

/// <summary>
/// Request for <c>rdcore/host/debug/goto</c>: moves the program counter of the activation the program waits in (<c>Set Next Statement</c>), so that it goes on from
/// there when it is resumed and not from where it stopped. Nothing runs.
/// </summary>
/// <remarks>
/// A program does not always go on from exactly where it stopped: the person looking at it may move the point of execution, or the statement it was to run next may
/// be one nobody wants run. The target is within the procedure the program waits in, which is the one whose code is in front of whoever asks - a line is a line
/// of that procedure as it was when the program stopped. Anything else is <see cref="HostDebugGotoResult.Moved"/> false, and the program still waits where it was.
/// <para>
/// Moving into or out of a block statement is moving as <c>GoTo</c> does: the state a block keeps (a <c>With</c> object, a <c>For</c> counter) is the state it had.
/// </para>
/// </remarks>
[Method(RDCorePlatformProtocol.HostDebugGoto, Direction.ClientToServer)]
public record class HostDebugGotoParams : IRequest, IRequest<HostDebugGotoResult>
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
/// Request for <c>rdcore/host/debug/editStatus</c>: which edits made while a program waits are not applied to it, and why - what resuming it, stepping it or moving
/// its point of execution would be refused for (<see cref="HostDebugEditStatusResult"/>). A client asks after each edit it sends, to say so of the edit as it is
/// made, before the program is asked to go on.
/// </summary>
[Method(RDCorePlatformProtocol.HostDebugEditStatus, Direction.ClientToServer)]
public record class HostDebugEditStatusParams : IRequest, IRequest<HostDebugEditStatusResult>;

/// <summary>
/// Request for <c>rdcore/host/debug/breakpoints</c>: sets the lines of a module a program that runs under a debugger waits at before it runs them. Replaces the
/// breakpoints the module had (<see cref="HostDebugBreakpointsResult"/>).
/// </summary>
[Method(RDCorePlatformProtocol.HostDebugBreakpoints, Direction.ClientToServer)]
public record class HostDebugBreakpointsParams : IRequest, IRequest<HostDebugBreakpointsResult>
{
    /// <summary>The programmatic name of the module.</summary>
    public string ModuleName { get; init; } = string.Empty;

    /// <summary>The breakpoints, one per line of the source. None removes the module's breakpoints.</summary>
    public IReadOnlyList<HostBreakpointLine> Breakpoints { get; init; } = [];
}

/// <summary>
/// A breakpoint asked of the host: a line, whether it is on, and the condition it waits on.
/// </summary>
/// <remarks>
/// A disabled breakpoint is sent all the same: its line is judged like any other's (<see cref="HostBreakpoint.Verified"/>), and the program does not wait at it.
/// </remarks>
public record class HostBreakpointLine
{
    /// <summary>The zero-based line of the source.</summary>
    public int Line { get; init; }

    /// <summary>Whether the program waits at the line. A disabled breakpoint is kept, and is not waited at.</summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>
    /// The <see cref="System.Text.Json"/> representation of the condition, an <c>ExpressionNode</c> (see <see cref="PlatformJson"/>), or <see langword="null"/> for a
    /// breakpoint that is always waited at. It travels as a parsed tree for the reason <see cref="HostDebugEvaluateParams.Json"/> does.
    /// </summary>
    public string? ConditionJson { get; init; }
}

/// <summary>
/// Request for <c>rdcore/host/debug/stack</c>: the activations of the program that waits, innermost first.
/// </summary>
[Method(RDCorePlatformProtocol.HostDebugStack, Direction.ClientToServer)]
public record class HostDebugStackParams : IRequest, IRequest<HostDebugStackResult>;

/// <summary>
/// Request for <c>rdcore/host/debug/variables</c>: the variables of an activation of the program that waits, or the parts of one of them.
/// </summary>
[Method(RDCorePlatformProtocol.HostDebugVariables, Direction.ClientToServer)]
public record class HostDebugVariablesParams : IRequest, IRequest<HostDebugVariablesResult>
{
    /// <summary>The activation, by <see cref="HostStackFrame.Id"/>. Ignored when <see cref="Reference"/> is given.</summary>
    public int FrameId { get; init; }

    /// <summary>Which variables of the activation.</summary>
    public HostVariableScope Scope { get; init; }

    /// <summary>
    /// The <see cref="HostVariable.Reference"/> of a variable that has parts - the elements of an array, the fields of a user-defined type - to get them instead.
    /// <c>0</c> for the variables of <see cref="Scope"/>.
    /// </summary>
    public int Reference { get; init; }
}

/// <summary>
/// Request for <c>rdcore/host/debug/evaluate</c>: the value of an expression in an activation of the program that waits, as if it were written in the procedure it is
/// an activation of, at the place it waits at.
/// </summary>
/// <remarks>
/// The expression travels as a parsed tree for the reason <see cref="HostExecuteParams.Json"/> does. Its names are looked up from the procedure of the activation - its
/// locals and parameters, then its module, then the project - and a call in it is made, in the session as it is: what the expression does stays done, as it does in the
/// immediate window of the VBA editor. A <c>Stop</c> in it stops it and not the program.
/// </remarks>
[Method(RDCorePlatformProtocol.HostDebugEvaluate, Direction.ClientToServer)]
public record class HostDebugEvaluateParams : IRequest, IRequest<HostDebugEvaluateResult>
{
    /// <summary>The activation, by its <see cref="HostStackFrame.Id"/>.</summary>
    public int FrameId { get; init; }

    /// <summary>The <see cref="System.Text.Json"/> representation of the expression, an <c>ExpressionNode</c> (see <see cref="PlatformJson"/>).</summary>
    public string Json { get; init; } = string.Empty;
}

/// <summary>
/// Request for <c>rdcore/host/debug/execute</c>: runs a statement in an activation of the program that waits, as if it were written in the procedure that activation is
/// of - the answer to a line typed in the immediate window of a procedure that is stopped.
/// </summary>
/// <remarks>
/// What it does stays done: an assignment to a local is the value the program goes on with. It is answered as <c>rdcore/host/debug/evaluate</c> is, with no value.
/// </remarks>
[Method(RDCorePlatformProtocol.HostDebugExecute, Direction.ClientToServer)]
public record class HostDebugExecuteParams : IRequest, IRequest<HostDebugEvaluateResult>
{
    /// <summary>The activation, by its <see cref="HostStackFrame.Id"/>.</summary>
    public int FrameId { get; init; }

    /// <summary>The <see cref="System.Text.Json"/> representation of the statement, a <c>SyntaxNode</c> (see <see cref="PlatformJson"/>).</summary>
    public string Json { get; init; } = string.Empty;
}
