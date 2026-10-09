using RDCore.SDK.Client;

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
/// What the shell knows of the program it runs under a debugger: its state, the line it waits before, and the lines that have breakpoints.
/// </summary>
/// <remarks>
/// A breakpoint is on a line number, as everything the person at a BASIC prompt says about their program is. It is on the line and not on the statement
/// that is written there, so it stays when the line is typed again; it goes when the line does, and the program is cleared (<c>NEW</c>, <c>LOAD</c>) with them.
/// </remarks>
public sealed class ReplDebugger
{
    private readonly SortedSet<int> _breakpoints = [];

    /// <summary>Whether a program runs or waits.</summary>
    public ReplDebugState State { get; private set; }

    /// <summary>The line number the program waits before, when it waits and the line is one the program was typed in.</summary>
    public int? StoppedAt { get; private set; }

    /// <summary>The line numbers that have a breakpoint, in order.</summary>
    public IReadOnlyCollection<int> Breakpoints => _breakpoints;

    /// <summary>
    /// Sets the breakpoint of a line, or clears it if it has one.
    /// </summary>
    /// <param name="number">The line number.</param>
    /// <returns>Whether the line has a breakpoint now.</returns>
    public bool Toggle(int number) => !_breakpoints.Remove(number) && _breakpoints.Add(number);

    /// <summary>
    /// Clears every breakpoint.
    /// </summary>
    public void ClearBreakpoints() => _breakpoints.Clear();

    /// <summary>
    /// The activation <c>VARS</c> and <c>EVAL</c> are about, by its place on the stack: the innermost, <c>0</c>, until <c>FRAME</c> selects another. It is the innermost
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
    public async Task PushBreakpointsAsync(ReplCommandContext context, CancellationToken token)
    {
        _ = _breakpoints.RemoveWhere(number => context.Program.IndexOf(number) < 0);
        if (!context.Platform.Provides<ProgramDebugging>())
        {
            return;
        }

        var lines = _breakpoints.Select(number => context.Program.SourceLineOf(number)).ToArray();
        _ = await context.Platform.SetBreakpointsAsync(ReplProgram.ModuleName, lines, token);
    }
}
