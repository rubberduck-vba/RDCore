using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace RDCore.SDK.Semantics.Plans;

/// <summary>
/// The plans last asked for, each in a set chosen by the identity of what it was asked for: what an operation evaluated in a loop asks with every time.
/// </summary>
/// <remarks>
/// Finding a plan by the references it was asked for is several times cheaper than looking it up by value, which compares the records the types are.
/// <para>
/// The set of a plan is chosen by identity hashes, which nothing chooses: two plans a loop asks for in turn can share one. A set therefore keeps the two
/// plans added to it last, the latest first, so that two such plans are both kept rather than evicting each other on every iteration; a set that holds
/// neither is a miss, and the plan is looked up by value.
/// </para>
/// An entry is immutable and replaces the one before it whole, so threads that race for a set only cost each other a lookup.
/// </remarks>
/// <typeparam name="TPlan">The plan.</typeparam>
internal sealed class RecentPlans<TPlan> where TPlan : class
{
    private const int Sets = 64;

    private sealed record class Entry(object First, object Second, object? Third, TPlan Plan);

    // a set is two entries side by side: the one added last, and the one added before it.
    private readonly Entry?[] _entries = new Entry?[Sets * 2];

    /// <summary>
    /// The plan last asked for with these very references, if it is still in its set.
    /// </summary>
    public bool TryGet(object first, object second, object? third, [MaybeNullWhen(false)] out TPlan plan)
    {
        var set = SetOf(first, second, third);
        return Matches(_entries[set], first, second, third, out plan) || Matches(_entries[set + 1], first, second, third, out plan);
    }

    /// <summary>
    /// Keeps the plan that was asked for with these references, first in its set, in place of the entry its set held before the last one.
    /// </summary>
    public void Add(object first, object second, object? third, TPlan plan)
    {
        var set = SetOf(first, second, third);
        _entries[set + 1] = _entries[set];
        _entries[set] = new Entry(first, second, third, plan);
    }

    private static bool Matches(Entry? entry, object first, object second, object? third, [MaybeNullWhen(false)] out TPlan plan)
    {
        if (entry is not null && ReferenceEquals(entry.First, first) && ReferenceEquals(entry.Second, second) && ReferenceEquals(entry.Third, third))
        {
            plan = entry.Plan;
            return true;
        }

        plan = null;
        return false;
    }

    // the index of the first entry of the set. The hashes are mixed thoroughly: the plans of a loop are asked for with pairs of the same few references,
    // (Long, Long), (Long, Double), (Double, Double), which a mix that keeps few of their bits sends to one set far more often than chance would.
    private static int SetOf(object first, object second, object? third)
        => (HashCode.Combine(RuntimeHelpers.GetHashCode(first), RuntimeHelpers.GetHashCode(second), third is null ? 0 : RuntimeHelpers.GetHashCode(third)) & (Sets - 1)) * 2;
}
