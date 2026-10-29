using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace RDCore.SDK.Workspace;

/// <summary>
/// What each module's last load failed with, so that what is said of a load is said once: a module that fails to load as it failed the last time - an editor
/// sends a module to load again at every edit - is not reported again, and one that loads after it failed is reported once to load again.
/// </summary>
/// <remarks>
/// A module is known by the key its component gives it, without regard to case - the language server keys a module by its address, and a shell's program
/// apart from the module of the workspace that has its name; a failure, by its errors in the order they were reported. Each component that loads modules and logs
/// their failures - the environment host, the language server that sends them - keeps one of these for each session it loads them in, so that a session
/// composed anew reports each failure again.
/// </remarks>
public sealed class ModuleLoadFailures
{
    private readonly ConcurrentDictionary<string, ImmutableArray<string>> _last = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Records what a load of <paramref name="module"/> ended with, and says what of it is news.
    /// </summary>
    /// <param name="module">The key of the module.</param>
    /// <param name="errors">What the load failed with; empty when the module loaded.</param>
    public ModuleLoadChange Record(string module, IReadOnlyList<string> errors)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(errors);

        if (errors.Count == 0)
        {
            return _last.TryRemove(module, out _) ? ModuleLoadChange.Recovered : ModuleLoadChange.Unchanged;
        }

        ImmutableArray<string> failure = [.. errors];
        var previous = default(ImmutableArray<string>);
        _last.AddOrUpdate(module, failure, (_, before) =>
        {
            previous = before;
            return failure;
        });

        return !previous.IsDefault && previous.SequenceEqual(failure, StringComparer.Ordinal) ? ModuleLoadChange.Unchanged : ModuleLoadChange.Failed;
    }
}

/// <summary>
/// What a load of a module says that the one before it did not (<see cref="ModuleLoadFailures.Record"/>).
/// </summary>
public enum ModuleLoadChange
{
    /// <summary>Nothing: the module loaded as it did, or failed with the errors it failed with the last time.</summary>
    Unchanged,

    /// <summary>The module failed to load, and the last load did not fail with these errors: they are to be reported.</summary>
    Failed,

    /// <summary>The module loaded, and the last load failed: that it loads again is to be reported.</summary>
    Recovered,
}
