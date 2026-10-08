using MediatR;
using OmniSharp.Extensions.JsonRpc;
using RDCore.SDK.Client;
using RDCore.SDK.Runtime.Abstract.Execution;

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
[Method(RDCorePlatformProtocol.HostDebugPause, Direction.ClientToServer)]
public record class HostDebugPauseParams : IRequest, IRequest<HostDebugAck>;

/// <summary>
/// Request for <c>rdcore/host/debug/terminate</c>: ends a program that is running or waits. Its variables stay, as they do when a program is stopped, unless
/// <see cref="Wipe"/> says they go.
/// </summary>
[Method(RDCorePlatformProtocol.HostDebugTerminate, Direction.ClientToServer)]
public record class HostDebugTerminateParams : IRequest, IRequest<HostDebugAck>
{
    /// <summary>
    /// Whether the session is wiped, as an <c>End</c> wipes it, once the program is ended: <c>NEW</c> and <c>LOAD</c> do, a restart does not.
    /// </summary>
    public bool Wipe { get; init; }
}

/// <summary>
/// What a request about the program that is being debugged answers when it has no result of its own.
/// </summary>
public record class HostDebugAck
{
    /// <summary>
    /// Whether there was a program to act on: a program was running or waiting, and now is paused or ended.
    /// </summary>
    public bool Acted { get; init; }
}
