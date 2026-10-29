using RDCore.SDK.Semantics.Instructions;

namespace RDCore.Tests.Semantics.Instructions;

/// <summary>
/// What an alignment says of each instruction of a body is what every longest common subsequence of the body and its edited body says of it: kept at
/// one place, removed, or neither. Each instruction is one letter here.
/// </summary>
[TestClass]
public sealed class InstructionAlignmentTests
{
    private static string[] Body(string letters) => [.. letters.Select(letter => letter.ToString())];

    // every longest common subsequence of the two, as the pairs of offsets it keeps.
    private static List<(int Old, int Now)[]> Longest(string[] old, string[] now)
    {
        var all = new List<(int, int)[]>();
        void Walk(int i, int j, List<(int, int)> kept)
        {
            all.Add([.. kept]);
            for (var a = i; a < old.Length; a++)
            {
                for (var b = j; b < now.Length; b++)
                {
                    if (old[a] == now[b])
                    {
                        kept.Add((a, b));
                        Walk(a + 1, b + 1, kept);
                        kept.RemoveAt(kept.Count - 1);
                    }
                }
            }
        }

        Walk(0, 0, []);
        var longest = all.Max(subsequence => subsequence.Length);
        return [.. all.Where(subsequence => subsequence.Length == longest)];
    }

    // what every longest common subsequence says of each instruction: where all of them keep it, -1 when none does, -2 otherwise.
    private static int[] Expected(string[] old, string[] now)
    {
        var longest = Longest(old, now);
        return [.. Enumerable.Range(0, old.Length).Select(i =>
        {
            var places = longest.Select(subsequence => subsequence.Where(pair => pair.Old == i).Select(pair => pair.Now).DefaultIfEmpty(-1).Single()).Distinct().ToList();
            return places.Count == 1 ? places[0] : -2;
        })];
    }

    [TestMethod]
    public void WhatItSaysOfEachInstruction_IsWhatEveryLongestCommonSubsequenceSays()
    {
        var random = new Random(20261029);
        for (var round = 0; round < 3000; round++)
        {
            var letters = "abcd"[..random.Next(2, 5)];
            var old = Body(new string([.. Enumerable.Range(0, random.Next(0, 8)).Select(_ => letters[random.Next(letters.Length)])]));
            var now = Body(new string([.. Enumerable.Range(0, random.Next(0, 8)).Select(_ => letters[random.Next(letters.Length)])]));

            CollectionAssert.AreEqual(Expected(old, now), InstructionAlignment.Place(old, now), $"{string.Concat(old)} -> {string.Concat(now)}");
        }
    }

    [TestMethod]
    [DataRow("aaa", "a", DisplayName = "one of three identical instructions kept")]
    [DataRow("asasap", "asap", DisplayName = "one of two identical pairs removed")]
    public void OfIdenticalInstructions_WhichWereKept_IsNotCertain(string old, string now)
    {
        var alignment = InstructionAlignment.Of(Body(old), Body(now));

        Assert.IsFalse(Enumerable.Range(0, old.Length - 1).All(alignment.IsCertain));
    }

    [TestMethod]
    public void TwoEdits_ThatLeaveOneOfTwoIdenticalPairsRemoved_LeaveWhereTheProgramGoesOnUncertain()
    {
        // a, Stop, a, Stop, a, Print edited into a, Stop, a, Print2: the pair removed, and the Print edited.
        var alignment = InstructionAlignment.Of(Body("asasap"), Body("asaq"));

        Assert.IsNull(alignment.After(3), "after the second Stop");
    }

    [TestMethod]
    public void ALineInsertedAfterTheStop_RunsAfterIt_AndOneEditedAboveIt_DoesNotRunAgain()
    {
        var alignment = InstructionAlignment.Of(Body("xsyz"), Body("Xsnyz"));

        Assert.AreEqual(2, alignment.After(1));
        Assert.AreEqual(3, alignment.Before(2));
    }
}
