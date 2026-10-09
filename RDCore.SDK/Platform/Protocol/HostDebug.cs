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

    /// <summary>
    /// Whether the host has the code of the module loaded, so that <see cref="HostBreakpoint.Verified"/> says what a line can have and not only what is not known yet.
    /// A line that is not verified in a result that is not judged may still become one when the code is loaded; in a result that is judged it is a line that cannot
    /// have a breakpoint, for the code as it is loaded.
    /// </summary>
    public bool Judged { get; init; }
}

/// <summary>
/// A breakpoint that was set.
/// </summary>
/// <param name="Line">The zero-based line it was asked for.</param>
/// <param name="Verified">Whether a statement of the module's loaded code begins on the line, which is what a program can wait before.</param>
public record class HostBreakpoint(int Line, bool Verified);

/// <summary>
/// Request for <c>rdcore/host/debug/stack</c>: the activations of the program that waits, innermost first.
/// </summary>
[Method(RDCorePlatformProtocol.HostDebugStack, Direction.ClientToServer)]
public record class HostDebugStackParams : IRequest, IRequest<HostDebugStackResult>;

/// <summary>
/// The call stack of a program that waits.
/// </summary>
public record class HostDebugStackResult
{
    /// <summary>The activations, innermost first. None when no program waits.</summary>
    public IReadOnlyList<HostStackFrame> Frames { get; init; } = [];
}

/// <summary>
/// One activation of a procedure.
/// </summary>
/// <param name="Id">Its place on the stack, with <c>0</c> the innermost: what the requests that are about a frame name it by, for as long as the program waits.</param>
/// <param name="Procedure">The procedure's name.</param>
/// <param name="Module">The name of the module that declares it.</param>
/// <param name="Line">The zero-based line of the statement the activation is at: the one it waits before for the innermost, the one that is calling for the others. <c>-1</c> when it is not known.</param>
/// <param name="Character">The zero-based column of that statement, or <c>-1</c>.</param>
/// <param name="ReturnLines">
/// The zero-based lines of the <c>GoSub</c> statements this activation has not <c>Return</c>ed from, innermost first: how it got to the line it is at. It is the GoSub
/// Resumption List of the activation (<strong>MS-VBAL 5.4.2.14</strong>), and is empty - and so says nothing - for the code that does not use <c>GoSub</c>.
/// </param>
/// <param name="Handler">
/// The label of the error handler this activation is running, when an error was caught by an <c>On Error GoTo</c> and the handler has not been left with a
/// <c>Resume</c> (<strong>MS-VBAL 5.4.4</strong>); <see langword="null"/> otherwise. The error itself is in the error metadata of the session, not here.
/// </param>
public record class HostStackFrame(int Id, string Procedure, string Module, int Line, int Character, IReadOnlyList<int>? ReturnLines = null, string? Handler = null)
{
    /// <summary>The lines of the <c>GoSub</c> statements this activation will return to; none for a procedure that is not in one.</summary>
    public IReadOnlyList<int> ReturnLines { get; init; } = ReturnLines ?? [];
}

/// <summary>
/// Which variables of a frame.
/// </summary>
public enum HostVariableScope
{
    /// <summary>The parameters and the local variables of the procedure, and the value it returns.</summary>
    Locals,

    /// <summary>The variables of the module the procedure is declared in.</summary>
    Module,
}

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
/// The variables that were asked for.
/// </summary>
public record class HostDebugVariablesResult
{
    /// <summary>The variables, in the order they are declared in. None when no program waits, or the reference is no longer one.</summary>
    public IReadOnlyList<HostVariable> Variables { get; init; } = [];

    /// <summary>Whether there are more parts than were listed: a large array is cut short.</summary>
    public int NotListed { get; init; }
}

/// <summary>
/// A variable, or a part of one.
/// </summary>
/// <param name="Name">Its name, or <c>(2)</c> for an element and the name of the field for a field.</param>
/// <param name="Value">Its value as the debugger shows it: a string quoted, a date between <c>#</c>. Empty for a variable whose value is its <see cref="Reference"/>.</param>
/// <param name="Type">Its type: <c>Long</c>, <c>Variant/String</c>, <c>Long(1 To 3)</c>.</param>
/// <param name="Reference">
/// Non-zero if it has parts to ask for, which is valid until the program is resumed or ended.
/// </param>
public record class HostVariable(string Name, string Value, string Type, int Reference = 0);

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

/// <summary>
/// What an expression came to.
/// </summary>
public record class HostDebugEvaluateResult
{
    /// <summary>Whether the expression had a value. When it did not, <see cref="Error"/> says why.</summary>
    public bool Success { get; init; }

    /// <summary>The value as a debugger shows it; empty for a value that is its parts.</summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>Its type.</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>Non-zero if the value has parts to ask for with <see cref="HostDebugVariablesParams.Reference"/>.</summary>
    public int Reference { get; init; }

    /// <summary>Why there is no value: no program waits, no such activation, a name that is not defined, an error the expression raised.</summary>
    public string? Error { get; init; }

    /// <summary>What a call in the expression printed, one entry per line: output for a debug console, and not the program's.</summary>
    public IReadOnlyList<string> Output { get; init; } = [];
}
