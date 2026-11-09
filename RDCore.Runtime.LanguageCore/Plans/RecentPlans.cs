using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace RDCore.Runtime.LanguageCore.Plans;

internal sealed class RecentPlans<TPlan> where TPlan : class
{
    private const int Sets = 64;

    private sealed record class Entry(object First, object Second, object? Third, TPlan Plan);

    // a set is two entries side by side: the one added last, and the one added before it.
    private readonly Entry?[] _entries = new Entry?[Sets * 2];

    public bool TryGet(object first, object second, object? third, [MaybeNullWhen(false)] out TPlan plan)
    {
        var set = SetOf(first, second, third);
        return Matches(_entries[set], first, second, third, out plan) || Matches(_entries[set + 1], first, second, third, out plan);
    }

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
