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
    private volatile ImmutableDictionary<string, ImmutableHashSet<int>> _lines = ImmutableDictionary<string, ImmutableHashSet<int>>.Empty;
    private int _version;
    private readonly ConditionalWeakTable<InstructionList, Resolved> _resolved = [];

    private sealed record Resolved(int Version, string Module, ImmutableHashSet<int> Offsets);

    /// <inheritdoc/>
    public bool HasAny => !_lines.IsEmpty;

    /// <inheritdoc/>
    public void Set(string module, IReadOnlyCollection<int> lines)
    {
        lock (_sync)
        {
            _lines = lines.Count == 0 ? _lines.Remove(module) : _lines.SetItem(module, [.. lines]);
            _version++;
        }
    }

    /// <inheritdoc/>
    public void Clear()
    {
        lock (_sync)
        {
            _lines = ImmutableDictionary<string, ImmutableHashSet<int>>.Empty;
            _version++;
        }
    }

    /// <inheritdoc/>
    public bool IsAt(string module, InstructionList body, int offset)
    {
        var version = Volatile.Read(ref _version);
        if (!_resolved.TryGetValue(body, out var resolved) || resolved.Version != version || resolved.Module != module)
        {
            resolved = new Resolved(version, module, Resolve(module, body));
            _resolved.AddOrUpdate(body, resolved);
        }

        return resolved.Offsets.Contains(offset);
    }

    // the first instruction of the body that begins on each of the module's lines.
    private ImmutableHashSet<int> Resolve(string module, InstructionList body)
    {
        if (!_lines.TryGetValue(module, out var lines))
        {
            return [];
        }

        var offsets = ImmutableHashSet.CreateBuilder<int>();
        var seen = new HashSet<int>();
        foreach (var instruction in body.Items)
        {
            if (instruction.Node is { } node && node.SourceLocation.Range.Start.Line is var line && lines.Contains(line) && seen.Add(line))
            {
                offsets.Add(instruction.Offset);
            }
        }

        return offsets.ToImmutable();
    }
}
