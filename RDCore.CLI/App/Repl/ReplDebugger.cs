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
    /// Clears every breakpoint, the program's and those of the modules of the workspace.
    /// </summary>
    public void ClearBreakpoints()
    {
        _breakpoints.Clear();
        ModuleBreakpoints.Clear();
    }

    /// <summary>
    /// The breakpoints of the modules of the workspace the shell is attached to, by the lines of each module.
    /// </summary>
    public ReplModuleBreakpoints ModuleBreakpoints { get; } = new();

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
    /// The breakpoints the platform refused, and why: the program's whose condition is not an expression, which are dropped whether or not the verdict is final,
    /// since what is not an expression is not one whatever the code is; and those of a module of the workspace whose condition is not one, or whose line no statement
    /// begins on. The code of a module is the code of its file, so the platform's verdict on its lines is final whenever it has one.
    /// </returns>
    public async Task<IReadOnlyList<ReplBreakpointRefusal>> PushBreakpointsAsync(ReplCommandContext context, CancellationToken token, bool judge = false)
    {
        _ = _breakpoints.RemoveWhere(breakpoint => context.Program.IndexOf(breakpoint.LineNumber) < 0);
        if (!context.Platform.Provides<ProgramDebugging>())
        {
            return [];
        }

        var refused = new List<ReplBreakpointRefusal>();
        var breakpoints = _breakpoints.ToArray();
        var result = await context.Platform.SetBreakpointsAsync(
            context.Program.Module, [.. breakpoints.Select(breakpoint => WireOf(breakpoint, context.Program.SourceLineOf(breakpoint.LineNumber)))], token);
        if (result.Breakpoints.Count == breakpoints.Length)
        {
            for (var i = 0; i < breakpoints.Length; i++)
            {
                if (result.Breakpoints[i].ConditionError is { } reason)
                {
                    _ = _breakpoints.Remove(breakpoints[i]);
                    refused.Add(new ReplBreakpointRefusal(breakpoints[i], Module: null, reason));
                }
                else if (judge && result.Judged && !result.Breakpoints[i].Verified)
                {
                    _ = _breakpoints.Remove(breakpoints[i]);
                }
            }
        }

        // every module that had a breakpoint is told, one that has none now included: it is how the platform is told they are gone.
        foreach (var (module, moduleBreakpoints) in ModuleBreakpoints.ByModule())
        {
            var told = await context.Platform.SetBreakpointsAsync(
                module, [.. moduleBreakpoints.Select(breakpoint => WireOf(breakpoint, breakpoint.LineNumber - 1))], token);
            if (told.Breakpoints.Count != moduleBreakpoints.Length)
            {
                continue;
            }

            for (var i = 0; i < moduleBreakpoints.Length; i++)
            {
                if (told.Breakpoints[i].ConditionError is { } reason)
                {
                    ModuleBreakpoints.Remove(module, moduleBreakpoints[i].LineNumber);
                    refused.Add(new ReplBreakpointRefusal(moduleBreakpoints[i], module, reason));
                }
                else if (told.Judged && !told.Breakpoints[i].Verified)
                {
                    ModuleBreakpoints.Remove(module, moduleBreakpoints[i].LineNumber);
                    refused.Add(new ReplBreakpointRefusal(moduleBreakpoints[i], module, ConditionError: null));
                }
            }
        }

        return refused;
    }

    private static SessionBreakpointLine WireOf(Breakpoint breakpoint, int sourceLine)
        => new() { Line = sourceLine, IsEnabled = breakpoint.Enabled, Condition = breakpoint.ConditionExpression };
}

/// <summary>
/// A breakpoint the platform refused, and why.
/// </summary>
/// <param name="Breakpoint">The breakpoint, by the line number of the program or the line of its module.</param>
/// <param name="Module">The module of the workspace it is in; <see langword="null"/> for one of the program.</param>
/// <param name="ConditionError">Why its condition is not an expression; <see langword="null"/> when it was refused because no statement begins on its line.</param>
public readonly record struct ReplBreakpointRefusal(Breakpoint Breakpoint, string? Module, string? ConditionError)
{
    /// <summary>
    /// Where the breakpoint is, as <c>BREAK</c> is told it: the line number of the program, or the module and its line.
    /// </summary>
    public string Where => ReplModuleBreakpoints.Where(Module, Breakpoint.LineNumber);

    /// <summary>
    /// What the shell says of the refusal.
    /// </summary>
    public string Message => ConditionError is { } reason
        ? string.Format(Resources.Repl_Breakpoint_ConditionRefused, Where, reason)
        : string.Format(Resources.Repl_Breakpoint_NoStatement, Where);
}

/// <summary>
/// The breakpoints of the modules of a workspace, each on a line of its module, one-based as a listing numbers the lines.
/// </summary>
/// <remarks>
/// A module's name is matched in any case, as VBA matches names, and is kept as it was first given. A module whose breakpoints were all removed is kept, with
/// none, until the platform is told (<see cref="ByModule"/>).
/// </remarks>
public sealed class ReplModuleBreakpoints
{
    private readonly Dictionary<string, SortedSet<Breakpoint>> _byModule = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Where a breakpoint is, as <c>BREAK</c> is told it and lists it: the line number of the program, or the module and its line (<c>Other 9</c>).
    /// </summary>
    /// <param name="module">The module of the workspace; <see langword="null"/> for the program.</param>
    /// <param name="line">The line number of the program, or the line of the module.</param>
    public static string Where(string? module, int line) => module is null ? line.ToString() : $"{module} {line}";

    /// <summary>
    /// The number of breakpoints, in every module.
    /// </summary>
    public int Count => _byModule.Values.Sum(breakpoints => breakpoints.Count);

    /// <summary>
    /// Every breakpoint, by module and line.
    /// </summary>
    public IEnumerable<(string Module, Breakpoint Breakpoint)> All
        => _byModule.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase).SelectMany(entry => entry.Value.Select(breakpoint => (entry.Key, breakpoint)));

    /// <summary>
    /// The breakpoints of a module.
    /// </summary>
    /// <param name="module">The module's name, in any case.</param>
    public IReadOnlyCollection<Breakpoint> Of(string module) => _byModule.TryGetValue(module, out var breakpoints) ? breakpoints : [];

    /// <summary>
    /// The breakpoint of a line of a module, when it has one.
    /// </summary>
    /// <param name="module">The module's name, in any case.</param>
    /// <param name="line">The line of the module.</param>
    /// <param name="breakpoint">The breakpoint.</param>
    public bool TryGet(string module, int line, out Breakpoint breakpoint)
    {
        breakpoint = default;
        return _byModule.TryGetValue(module, out var breakpoints) && breakpoints.TryGetValue(new(line), out breakpoint);
    }

    /// <summary>
    /// Sets the breakpoint of a line of a module, or clears it if it has one.
    /// </summary>
    /// <param name="module">The module's name.</param>
    /// <param name="line">The line of the module.</param>
    /// <returns>Whether the line has a breakpoint now.</returns>
    public bool Toggle(string module, int line) => !Breakpoints(module).Remove(new(line)) && Breakpoints(module).Add(new(line));

    /// <summary>
    /// Sets a breakpoint of a module, replacing the one its line had.
    /// </summary>
    /// <param name="module">The module's name.</param>
    /// <param name="breakpoint">The breakpoint.</param>
    public void Set(string module, Breakpoint breakpoint)
    {
        _ = Breakpoints(module).Remove(breakpoint);
        _ = Breakpoints(module).Add(breakpoint);
    }

    /// <summary>
    /// Disables the breakpoint of a line of a module, or enables it if it is disabled; a line that has none is given one, enabled.
    /// </summary>
    /// <param name="module">The module's name.</param>
    /// <param name="line">The line of the module.</param>
    /// <returns>Whether the line has an enabled breakpoint now.</returns>
    public bool ToggleEnabled(string module, int line)
    {
        var toggled = TryGet(module, line, out var breakpoint) ? breakpoint with { Enabled = !breakpoint.Enabled } : new Breakpoint(line);
        Set(module, toggled);
        return toggled.Enabled;
    }

    /// <summary>
    /// Removes the breakpoint of a line of a module.
    /// </summary>
    /// <param name="module">The module's name.</param>
    /// <param name="line">The line of the module.</param>
    public void Remove(string module, int line)
    {
        if (_byModule.TryGetValue(module, out var breakpoints))
        {
            _ = breakpoints.Remove(new(line));
        }
    }

    /// <summary>
    /// Removes every breakpoint of every module.
    /// </summary>
    public void Clear()
    {
        foreach (var breakpoints in _byModule.Values)
        {
            breakpoints.Clear();
        }
    }

    /// <summary>
    /// The breakpoints of each module that has had one, for the platform to be told them; a module that has none now is then forgotten.
    /// </summary>
    public IEnumerable<(string Module, Breakpoint[] Breakpoints)> ByModule()
    {
        var modules = _byModule.Select(entry => (entry.Key, entry.Value.ToArray())).ToArray();
        foreach (var (module, _) in modules.Where(entry => entry.Item2.Length == 0))
        {
            _byModule.Remove(module);
        }

        return modules;
    }

    private SortedSet<Breakpoint> Breakpoints(string module)
    {
        if (!_byModule.TryGetValue(module, out var breakpoints))
        {
            _byModule[module] = breakpoints = [];
        }

        return breakpoints;
    }
}
