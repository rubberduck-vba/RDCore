using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics.Instructions;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace RDCore.Runtime.Execution;

/// <inheritdoc cref="IBreakpointTable"/>
/// <remarks>
/// Which offsets of a body are breakpoints is worked out the first time the interpreter asks about the body after the breakpoints changed, and not again: between
/// two instructions it is a lookup. Bodies are immutable and a module that is loaded again has new ones, so the answer is cached by the body itself.
/// </remarks>
public sealed class BreakpointTable : IBreakpointTable
{
    private readonly Lock _sync = new();
    private volatile ImmutableDictionary<string, ImmutableDictionary<int, ExpressionNode?>> _lines = ImmutableDictionary<string, ImmutableDictionary<int, ExpressionNode?>>.Empty;
    private int _version;
    private readonly ConditionalWeakTable<InstructionList, Resolved> _resolved = [];

    // the conditions by offset: a breakpoint with no condition is an offset whose value is null.
    private sealed record Resolved(int Version, string Module, ImmutableDictionary<int, ExpressionNode?> Offsets);

    /// <inheritdoc/>
    public bool HasAny => !_lines.IsEmpty;

    /// <inheritdoc/>
    public void Set(string module, IReadOnlyCollection<RuntimeBreakpoint> breakpoints)
    {
        // one breakpoint per line: a line asked for twice is the last one asked.
        var lines = ImmutableDictionary.CreateBuilder<int, ExpressionNode?>();
        foreach (var breakpoint in breakpoints)
        {
            lines[breakpoint.Line] = breakpoint.Condition;
        }

        lock (_sync)
        {
            _lines = lines.Count == 0 ? _lines.Remove(module) : _lines.SetItem(module, lines.ToImmutable());
            _version++;
        }
    }

    /// <inheritdoc/>
    public void Clear()
    {
        lock (_sync)
        {
            _lines = ImmutableDictionary<string, ImmutableDictionary<int, ExpressionNode?>>.Empty;
            _version++;
        }
    }

    /// <inheritdoc/>
    public bool IsAt(string module, InstructionList body, int offset, out ExpressionNode? condition)
    {
        var version = Volatile.Read(ref _version);
        if (!_resolved.TryGetValue(body, out var resolved) || resolved.Version != version || resolved.Module != module)
        {
            resolved = new Resolved(version, module, Resolve(module, body));
            _resolved.AddOrUpdate(body, resolved);
        }

        return resolved.Offsets.TryGetValue(offset, out condition);
    }

    // the first instruction of the body that is on each of the module's lines.
    private ImmutableDictionary<int, ExpressionNode?> Resolve(string module, InstructionList body)
    {
        if (!_lines.TryGetValue(module, out var lines))
        {
            return ImmutableDictionary<int, ExpressionNode?>.Empty;
        }

        var offsets = ImmutableDictionary.CreateBuilder<int, ExpressionNode?>();
        var seen = new HashSet<int>();
        foreach (var instruction in body.Items)
        {
            if (LineOf(instruction) is { } line && lines.TryGetValue(line, out var condition) && seen.Add(line))
            {
                offsets[instruction.Offset] = condition;
            }
        }

        return offsets.ToImmutable();
    }

    // the line a program waits on before the instruction: its statement's, or the closing line of the block it closes (a Next, a Loop, an End If, the
    // procedure's End Sub), each of which MS-VBA stops on. None for the jump that ends a branch, which a program goes through.
    public static int? LineOf(Instruction instruction) => instruction.Location?.Range.Start.Line;

    /// <summary>
    /// Whether a line is one a program can wait at: an instruction of one of the bodies is on it (<see cref="LineOf"/>).
    /// </summary>
    /// <param name="bodies">The bodies of the module's procedures.</param>
    /// <param name="line">A zero-based line of the source.</param>
    public static bool Verify(IEnumerable<InstructionList> bodies, int line)
        => bodies.Any(body => body.Items.Any(instruction => LineOf(instruction) == line));

    // where a move of the program counter to the line lands: the first instruction on it, or else on the first line after it that has one.
    public static bool TryGetOffsetAtLine(InstructionList body, int line, out int offset)
    {
        offset = -1;
        var best = int.MaxValue;
        foreach (var instruction in body.Items)
        {
            if (LineOf(instruction) is { } start && start >= line && start < best)
            {
                (best, offset) = (start, instruction.Offset);
            }
        }

        return offset >= 0;
    }
}
