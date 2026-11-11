using RDCore.SDK.Client;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.CLI.App.Repl;

/// <summary>
/// Whether the program the shell ran is running, or waits where it stopped.
/// </summary>
public enum ReplDebugState
{
    /// <summary>There is no program that runs or waits.</summary>
    Idle,

    /// <summary>A program is running, and the shell is waiting for it.</summary>
    Running,

    /// <summary>A program waits where a <c>STOP</c>, a breakpoint or a break at the keyboard stopped it.</summary>
    Suspended,
}

/// <summary>
/// A breakpoint of the shell's program: a line number, whether it is on, and the condition it waits on.
/// </summary>
/// <remarks>
/// A breakpoint is its line: two are the same breakpoint when they are on the same line, whatever else they say.
/// </remarks>
/// <param name="LineNumber">The line number.</param>
/// <param name="Enabled">Whether the program waits at it. A disabled breakpoint is kept, and the program goes past it.</param>
/// <param name="ConditionExpression">The expression the program waits on, as it was typed; <see langword="null"/> when it always waits.</param>
public record struct Breakpoint(int LineNumber, bool Enabled = true, string? ConditionExpression = default) : IComparable<Breakpoint>, IEquatable<Breakpoint>
{
    public readonly int CompareTo(Breakpoint other) => LineNumber.CompareTo(other.LineNumber);
    public readonly override int GetHashCode() => LineNumber;
    public readonly bool Equals(Breakpoint other) => LineNumber == other.LineNumber;
    public static implicit operator int(Breakpoint breakpoint) => breakpoint.LineNumber;
}

/// <summary>
/// What the shell knows of the program it runs under a debugger: its state, the line it waits before, and the lines that have breakpoints.
/// </summary>
/// <remarks>
/// A breakpoint is on a line number, as everything the person at a BASIC prompt says about their program is. It is on the line and not on the statement
/// that is written there, so it stays when the line is typed again; it goes when the line does, and the program is cleared (<c>NEW</c>, <c>LOAD</c>) with them.
/// </remarks>
public sealed class ReplDebugger
{
    private readonly SortedSet<Breakpoint> _breakpoints = [];

    /// <summary>Whether a program runs or waits.</summary>
    public ReplDebugState State { get; private set; }

    /// <summary>The line number the program waits before, when it waits and the line is one the program was typed in.</summary>
    public int? StoppedAt { get; private set; }

    /// <summary>The line numbers that have a breakpoint, in order.</summary>
    public IReadOnlyCollection<Breakpoint> Breakpoints => _breakpoints;

    /// <summary>
    /// Sets the breakpoint of a line, or clears it if it has one.
    /// </summary>
    /// <param name="number">The line number.</param>
    /// <returns>Whether the line has a breakpoint now.</returns>
    public bool ToggleBreakpoint(int number) => !_breakpoints.Remove(new(number)) && _breakpoints.Add(new(number));

    /// <summary>
    /// Sets a breakpoint, replacing the one its line had.
    /// </summary>
    /// <param name="breakpoint">The breakpoint.</param>
    public void SetBreakpoint(Breakpoint breakpoint)
    {
        _ = _breakpoints.Remove(breakpoint);
        _ = _breakpoints.Add(breakpoint);
    }

    /// <summary>
    /// The breakpoint of a line, when it has one.
    /// </summary>
    /// <param name="number">The line number.</param>
    /// <param name="breakpoint">The breakpoint.</param>
    public bool TryGetBreakpoint(int number, out Breakpoint breakpoint) => _breakpoints.TryGetValue(new(number), out breakpoint);

    /// <summary>
    /// Disables the breakpoint of a line, or enables it if it is disabled; a line that has none is given one, enabled.
    /// </summary>
    /// <param name="number">The line number.</param>
    /// <returns>Whether the line has an enabled breakpoint now.</returns>
    public bool ToggleEnabled(int number)
    {
        var toggled = TryGetBreakpoint(number, out var breakpoint) ? breakpoint with { Enabled = !breakpoint.Enabled } : new Breakpoint(number);
        SetBreakpoint(toggled);
        return toggled.Enabled;
    }

    /// <summary>
    /// Clears every breakpoint.
    /// </summary>
    public void ClearBreakpoints() => _breakpoints.Clear();

    /// <summary>
    /// The activation <c>VARS</c> is about, by its place on the stack: the innermost, <c>0</c>, until <c>FRAME</c> selects another. It is the innermost
    /// again every time the program goes on, since what was selected is then not there.
    /// </summary>
    public int SelectedFrame { get; set; }

    /// <summary>The program was started or resumed, and the shell waits for it.</summary>
    public void Running() => (State, StoppedAt, SelectedFrame) = (ReplDebugState.Running, null, 0);

    /// <summary>The program stopped and waits.</summary>
    /// <param name="number">The line number it waits before, when it is known.</param>
    public void Suspended(int? number) => (State, StoppedAt) = (ReplDebugState.Suspended, number);

    /// <summary>The program is over, or was ended.</summary>
    public void Idle() => (State, StoppedAt) = (ReplDebugState.Idle, null);

    /// <summary>
    /// Tells the language server which lines of the program have breakpoints, as the lines of the module the program is run as.
    /// </summary>
    /// <remarks>
    /// Said before the program runs or goes on, and not when a breakpoint is set: the module is not in the session until the program is run, and a line typed
    /// since has moved nothing, since a line number is a line's identity. A breakpoint on a line that is gone goes with it.
    /// </remarks>
    /// <param name="context">The live session.</param>
    /// <param name="token">A token that cancels the request.</param>
    /// <param name="judge">
    /// Whether the platform's verdict is final: the code it holds is the program as it is typed now, because the program has just been run. A breakpoint on a line no
    /// statement begins on is then dropped. It is not before: until the program is run the platform holds the program it ran last, and a line typed since has no
    /// statement in it yet.
    /// </param>
    /// <returns>
    /// The breakpoints whose condition the platform refused - it is not an expression - and why. They are dropped whether or not the verdict is final: what is not an
    /// expression is not one whatever the code is.
    /// </returns>
    public async Task<IReadOnlyList<(Breakpoint Breakpoint, string Reason)>> PushBreakpointsAsync(ReplCommandContext context, CancellationToken token, bool judge = false)
    {
        _ = _breakpoints.RemoveWhere(breakpoint => context.Program.IndexOf(breakpoint.LineNumber) < 0);
        if (!context.Platform.Provides<ProgramDebugging>())
        {
            return [];
        }

        var breakpoints = _breakpoints.ToArray();
        var wireBreakpoints = breakpoints.Select(breakpoint => new SessionBreakpointLine
        {
            Line = context.Program.SourceLineOf(breakpoint.LineNumber),
            IsEnabled = breakpoint.Enabled,
            Condition = breakpoint.ConditionExpression
        }).ToArray();

        var result = await context.Platform.SetBreakpointsAsync(context.Program.Module, wireBreakpoints, token);
        if (result.Breakpoints.Count != breakpoints.Length)
        {
            return [];
        }

        var refused = new List<(Breakpoint, string)>();
        for (var i = 0; i < breakpoints.Length; i++)
        {
            if (result.Breakpoints[i].ConditionError is { } reason)
            {
                _ = _breakpoints.Remove(breakpoints[i]);
                refused.Add((breakpoints[i], reason));
            }
            else if (judge && result.Judged && !result.Breakpoints[i].Verified)
            {
                _ = _breakpoints.Remove(breakpoints[i]);
            }
        }

        return refused;
    }
}
