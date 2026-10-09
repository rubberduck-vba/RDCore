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

    /// <inheritdoc cref="HostDebugBreakpointsParams.Lines"/>
    public IReadOnlyList<int> Lines { get; init; } = [];
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
