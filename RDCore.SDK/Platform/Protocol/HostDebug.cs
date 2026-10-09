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
/// Where a program that waits goes on from, after a request to move it.
/// </summary>
public record class HostDebugGotoResult
{
    /// <summary>Whether the program counter was moved. When it was not, <see cref="Reason"/> says why, and the program waits where it did.</summary>
    public bool Moved { get; init; }

    /// <summary>Why it was not moved.</summary>
    public string? Reason { get; init; }

    /// <summary>The zero-based line of the statement the program now goes on from, or <c>-1</c> when it was not moved or the statement is the end of the procedure.</summary>
    public int Line { get; init; } = -1;

    /// <summary>The zero-based column of that statement, or <c>-1</c>.</summary>
    public int Character { get; init; } = -1;
}

/// <summary>
/// Request for <c>rdcore/host/debug/breakpoints</c>: sets the lines of a module a program that runs under a debugger waits at before it runs them. Replaces the
/// breakpoints the module had.
/// </summary>
/// <remarks>
/// A breakpoint is a line of the source and stays on it when the code is loaded again. A module whose code is not loaded yet can be given breakpoints all the same; they
/// are then not <see cref="HostBreakpoint.Verified"/>, which says they have not been found a statement yet, not that they will not be.
/// </remarks>
[Method(RDCorePlatformProtocol.HostDebugBreakpoints, Direction.ClientToServer)]
public record class HostDebugBreakpointsParams : IRequest, IRequest<HostDebugBreakpointsResult>
{
    /// <summary>The programmatic name of the module.</summary>
    public string ModuleName { get; init; } = string.Empty;

    /// <summary>The zero-based lines of the source. None removes the module's breakpoints.</summary>
    public IReadOnlyList<int> Lines { get; init; } = [];
}

/// <summary>
/// The breakpoints that were set, in the order they were asked for.
/// </summary>
public record class HostDebugBreakpointsResult
{
    /// <summary>One entry per line asked for.</summary>
    public IReadOnlyList<HostBreakpoint> Breakpoints { get; init; } = [];
}

/// <summary>
/// A breakpoint that was set.
/// </summary>
/// <param name="Line">The zero-based line it was asked for.</param>
/// <param name="Verified">Whether a statement of the module's loaded code begins on the line, which is what a program can wait before.</param>
public record class HostBreakpoint(int Line, bool Verified);
