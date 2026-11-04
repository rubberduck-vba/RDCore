using System.Collections.Immutable;
using RDCore.SDK.Model.Source;

namespace RDCore.SDK.Platform.Protocol;

/// <summary>
/// What a request about the program that is being debugged answers when it has no result of its own.
/// </summary>
public record class HostDebugAck
{
    /// <summary>
    /// Whether there was a program to act on: a program was running or waiting, and now is paused or ended.
    /// </summary>
    public bool Acted { get; init; }

    /// <summary>
    /// Whether the program was asked to end and had not stopped by the time the request was answered: it is in a call outside it - a <c>Declare</c>, an object, a
    /// dialog - and ends when the call returns. The end that was asked of it stands. Only <c>rdcore/host/debug/terminate</c> says so.
    /// </summary>
    public bool Stopping { get; init; }
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
/// The edits made while a program waits that are not applied to it.
/// </summary>
/// <remarks>
/// An edit reaches the program as it is loaded, while the program waits. Most are applied when the program goes on: the code of a procedure it is not in, a
/// declaration added, a value read afresh where it is next used. Those are not listed. What is listed is either applied by no build of the platform yet, or
/// can't be applied correctly at all - the signature of a procedure the program is in, a variable that holds a value given another type - and is picked up
/// when the program is run again.
/// </remarks>
public record class HostDebugEditStatusResult
{
    /// <summary>Whether a program waits. When none does, there is nothing for an edit to be applied to, and <see cref="Edits"/> is empty.</summary>
    public bool Suspended { get; init; }

    /// <summary>Each edit that is not applied to the program that waits; empty when the program can go on with every edit made.</summary>
    public ImmutableArray<HostDebugEdit> Edits { get; init; } = [];
}

/// <summary>
/// An edit made while a program waits that is not applied to it.
/// </summary>
public record class HostDebugEdit
{
    /// <summary>What was edited, as the user names it: <c>Module.Member</c>, or the module.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>What the edit is to the program.</summary>
    public HostDebugEditKind Kind { get; init; }

    /// <summary>Why, in the user's words.</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>The module the edit is in, as the host addresses it; <see langword="null"/> when it is not known.</summary>
    public Uri? ModuleUri { get; init; }

    /// <summary>
    /// Where the edited declaration or procedure is now, zero-based; <see langword="null"/> for an edit of the module as a whole (its options) or of a declaration
    /// that is gone.
    /// </summary>
    public SourceRange? Range { get; init; }
}

/// <summary>
/// The breakpoints that were set, in the order they were asked for.
/// </summary>
/// <remarks>
/// A breakpoint is a line of the source and stays on it when the code is loaded again. A module whose code is not loaded yet can be given breakpoints all the same;
/// they are then not <see cref="HostBreakpoint.Verified"/>, which says they have not been found a statement yet, not that they will not be.
/// </remarks>
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
/// <param name="ConditionError">
/// Why the breakpoint's condition was refused - it is not an expression - or <see langword="null"/> when it has none or it was accepted. A breakpoint whose condition is
/// refused is not waited at: a client is to tell the person why, and drop it.
/// </param>
public record class HostBreakpoint(int Line, bool Verified, string? ConditionError = null);

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

    /// <summary>Non-zero if the value has parts to ask for, as a variable's <see cref="HostVariable.Reference"/> is.</summary>
    public int Reference { get; init; }

    /// <summary>Why there is no value: no program waits, no such activation, a name that is not defined, an error the expression raised.</summary>
    public string? Error { get; init; }

    /// <summary>What a call in the expression printed, one entry per line: output for a debug console, and not the program's.</summary>
    public IReadOnlyList<string> Output { get; init; } = [];

    /// <summary>
    /// Whether what ran ended the program that waited - an <c>End</c> typed at the stop, or one in a procedure it called. The program is then over, and nothing waits
    /// any more; <see cref="Error"/> says so.
    /// </summary>
    public bool EndedTheProgram { get; init; }
}
