using RDCore.SDK.Semantics.Plans;

namespace RDCore.Tests.Semantics;

/// <summary>
/// The plans last asked for, found by the identity of what they were asked for. Where a plan is kept depends on identity hashes nobody chooses, so two
/// plans an operation in a loop asks for in turn can be given the same place; they must not take turns evicting each other.
/// </summary>
[TestClass]
public sealed class RecentPlansTests
{
    [TestMethod]
    public void APlanAdded_IsFoundByTheSameReferences()
    {
        var (first, second) = (new object(), new object());
        var recent = new RecentPlans<string>();
        recent.Add(first, second, null, "plan");

        Assert.IsTrue(recent.TryGet(first, second, null, out var plan));
        Assert.AreEqual("plan", plan);
        Assert.IsFalse(recent.TryGet(second, first, null, out _), "other references are another plan");
    }

    [TestMethod]
    public void TwoPlansAskedForInTurn_AreBothKept()
    {
        // among forty keys, some pairs are all but certain to share a place.
        var keys = Enumerable.Range(0, 40).Select(_ => new object()).ToArray();
        var destination = new object();
        foreach (var first in keys)
        {
            foreach (var second in keys.Where(key => key != first))
            {
                var recent = new RecentPlans<object>();
                recent.Add(first, destination, null, first);
                recent.Add(second, destination, null, second);

                Assert.IsTrue(recent.TryGet(first, destination, null, out var plan) && plan == first, "the plan added first was evicted");
                Assert.IsTrue(recent.TryGet(second, destination, null, out plan) && plan == second);
            }
        }
    }

    [TestMethod]
    public void ThePlansOfALoop_AreAlmostNeverEvicted()
    {
        // S = S + I with a Long I and a Double S asks for (Long, Long), (Long, Double) and (Double, Double): pairs of the same two references, which a
        // hash that mixes them poorly puts in one set far more often than chance would (one loop in fifty, rather than one in 4096). Three plans in a set
        // of two evict each other on every iteration.
        var evicted = 0;
        for (var trial = 0; trial < 2000; trial++)
        {
            var (a, b) = (new object(), new object());
            var recent = new RecentPlans<string>();
            recent.Add(a, a, null, "aa");
            recent.Add(a, b, null, "ab");
            recent.Add(b, b, null, "bb");
            if (!recent.TryGet(a, a, null, out _) || !recent.TryGet(a, b, null, out _) || !recent.TryGet(b, b, null, out _))
            {
                evicted++;
            }
        }

        Assert.IsTrue(evicted <= 8, $"the plans of {evicted} loops out of 2000 evicted each other");
    }
}
