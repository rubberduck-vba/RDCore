using MediatR;
using OmniSharp.Extensions.JsonRpc;
using RDCore.SDK.Client;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.SDK.Platform.Protocol;

/// <summary>
/// Request for <c>rdcore/session/debug/resume</c>: a client asks the language server to go on with the program it ran under a debugger (<see cref="ExecuteSessionParams.Debug"/>)
/// that waits where it stopped. The language-server side of <see cref="HostDebugResumeParams"/>.
/// </summary>
[Method(RDCorePlatformProtocol.SessionDebugResume, Direction.ClientToServer)]
public record class SessionDebugResumeParams : IRequest, IRequest<ExecuteSessionResult>
{
    /// <inheritdoc cref="HostDebugResumeParams.Step"/>
    public StepKind? Step { get; init; }
}

/// <summary>
/// Request for <c>rdcore/session/debug/pause</c>: stops the program that is running, at the next instruction, so that it waits there. The request that is running the
/// program answers <see cref="ExecutionOutcome.Suspended"/>. The language-server side of <see cref="HostDebugPauseParams"/>.
/// </summary>
/// <remarks>
/// This, and not cancelling the request that runs the program, is what a break at the keyboard is for a program under a debugger: a cancelled request has no answer to
/// read the place the program stopped at from.
/// </remarks>
[Method(RDCorePlatformProtocol.SessionDebugPause, Direction.ClientToServer)]
public record class SessionDebugPauseParams : IRequest, IRequest<HostDebugAck>;

/// <summary>
/// Request for <c>rdcore/session/debug/goto</c>: moves the point the program that waits goes on from. The language-server side of <see cref="HostDebugGotoParams"/>.
/// </summary>
[Method(RDCorePlatformProtocol.SessionDebugGoto, Direction.ClientToServer)]
public record class SessionDebugGotoParams : IRequest, IRequest<HostDebugGotoResult>
{
    /// <inheritdoc cref="HostDebugGotoParams.Line"/>
    public int Line { get; init; }

    /// <inheritdoc cref="HostDebugGotoParams.Label"/>
    public string? Label { get; init; }
}

/// <summary>
/// Request for <c>rdcore/session/debug/editStatus</c>: which edits made while a program waits are not applied to it. The language-server side of
/// <see cref="HostDebugEditStatusParams"/>.
/// </summary>
[Method(RDCorePlatformProtocol.SessionDebugEditStatus, Direction.ClientToServer)]
public record class SessionDebugEditStatusParams : IRequest, IRequest<HostDebugEditStatusResult>;

/// <summary>
/// Request for <c>rdcore/session/debug/terminate</c>: ends the program that is running or waits. The language-server side of <see cref="HostDebugTerminateParams"/>.
/// </summary>
[Method(RDCorePlatformProtocol.SessionDebugTerminate, Direction.ClientToServer)]
public record class SessionDebugTerminateParams : IRequest, IRequest<HostDebugAck>
{
    /// <inheritdoc cref="HostDebugTerminateParams.Wipe"/>
    public bool Wipe { get; init; }
}

/// <summary>
/// Request for <c>rdcore/session/debug/breakpoints</c>: sets the lines of a module that a program under a debugger waits at. The language-server side of
/// <see cref="HostDebugBreakpointsParams"/>.
/// </summary>
[Method(RDCorePlatformProtocol.SessionDebugBreakpoints, Direction.ClientToServer)]
public record class SessionDebugBreakpointsParams : IRequest, IRequest<HostDebugBreakpointsResult>
{
    /// <inheritdoc cref="HostDebugBreakpointsParams.ModuleName"/>
    public string ModuleName { get; init; } = string.Empty;

    /// <summary>The breakpoints, one per line of the source. None removes the module's breakpoints.</summary>
    public IReadOnlyList<SessionBreakpointLine> Breakpoints { get; init; } = [];
}

/// <summary>
/// A breakpoint as a client asks for it: a line, whether it is on, and the condition it waits on, as the person typed it.
/// </summary>
/// <remarks>
/// The language server parses the condition and sends the host the expression (<see cref="HostBreakpointLine.ConditionJson"/>). A condition that is not an expression
/// is refused there, and the result says why (<see cref="HostBreakpoint.ConditionError"/>); it is not evaluated until the program reaches the line, since what its
/// names are is the activation's that reaches it.
/// </remarks>
public record class SessionBreakpointLine
{
    /// <summary>The zero-based line of the source.</summary>
    public int Line { get; init; }

    /// <inheritdoc cref="HostBreakpointLine.IsEnabled"/>
    public bool IsEnabled { get; init; } = true;

    /// <summary>
    /// The source of the expression the program waits on when it reaches the line, a <c>Boolean</c> condition as an <c>If</c> has (see <see cref="RDCore.SDK.Runtime.Abstract.Execution.RuntimeBreakpoint.Condition"/>);
    /// <see langword="null"/> or blank for a breakpoint that is always waited at.
    /// </summary>
    public string? Condition { get; init; }
}

/// <summary>
/// Request for <c>rdcore/session/debug/stack</c>: the activations of the program that waits, innermost first. The language-server side of <see cref="HostDebugStackParams"/>.
/// </summary>
[Method(RDCorePlatformProtocol.SessionDebugStack, Direction.ClientToServer)]
public record class SessionDebugStackParams : IRequest, IRequest<HostDebugStackResult>;

/// <summary>
/// Request for <c>rdcore/session/debug/variables</c>: the variables of an activation of the program that waits, or the parts of one of them. The language-server side of
/// <see cref="HostDebugVariablesParams"/>.
/// </summary>
[Method(RDCorePlatformProtocol.SessionDebugVariables, Direction.ClientToServer)]
public record class SessionDebugVariablesParams : IRequest, IRequest<HostDebugVariablesResult>
{
    /// <inheritdoc cref="HostDebugVariablesParams.FrameId"/>
    public int FrameId { get; init; }

    /// <inheritdoc cref="HostDebugVariablesParams.Scope"/>
    public HostVariableScope Scope { get; init; }

    /// <inheritdoc cref="HostDebugVariablesParams.Reference"/>
    public int Reference { get; init; }
}

/// <summary>
/// Request for <c>rdcore/session/debug/evaluate</c>: the value of an expression, written as text, in an activation of the program that waits. The language server parses
/// it and has the host evaluate it. The language-server side of <see cref="HostDebugEvaluateParams"/>.
/// </summary>
[Method(RDCorePlatformProtocol.SessionDebugEvaluate, Direction.ClientToServer)]
public record class SessionDebugEvaluateParams : IRequest, IRequest<HostDebugEvaluateResult>
{
    /// <summary>The activation, by its <see cref="HostStackFrame.Id"/>.</summary>
    public int FrameId { get; init; }

    /// <summary>The expression, as it would be written in the procedure of the activation.</summary>
    public string Expression { get; init; } = string.Empty;
}

/// <summary>
/// Request for <c>rdcore/session/debug/execute</c>: runs a statement, written as text, in an activation of the program that waits - the answer to a line typed in the
/// immediate window of a procedure that is stopped. The language server parses it and has the host run it. The language-server side of
/// <see cref="HostDebugExecuteParams"/>.
/// </summary>
/// <remarks>
/// The statement's names are the activation's: a local of the procedure is the local the program goes on with, and a name the procedure does not see is not defined
/// - it declares nothing, since a statement typed at a stop is not part of the program's code. <c>?</c> is the immediate window's <c>Debug.Print</c>.
/// </remarks>
[Method(RDCorePlatformProtocol.SessionDebugExecute, Direction.ClientToServer)]
public record class SessionDebugExecuteParams : IRequest, IRequest<HostDebugEvaluateResult>
{
    /// <inheritdoc cref="SessionDebugEvaluateParams.FrameId"/>
    public int FrameId { get; init; }

    /// <summary>The statement, as it would be written in the procedure of the activation; one line.</summary>
    public string Statement { get; init; } = string.Empty;
}
