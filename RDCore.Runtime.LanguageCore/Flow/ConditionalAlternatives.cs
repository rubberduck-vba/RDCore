using RDCore.Runtime.LanguageCore.Instructions;
using RDCore.SDK.Semantics;

namespace RDCore.Runtime.LanguageCore.Flow;

/// <summary>
/// The branches of the <c>#If</c> blocks (<strong>MS-VBAL §3.4</strong>) of a body lowered with all of them, as the code paths go through them: at most one branch of
/// a block is compiled, so they are alternatives, never one after another.
/// </summary>
/// <remarks>
/// Control that comes to the first instruction of a branch from outside its block may go to the first of any branch of it, or past the block, when none is
/// compiled. Control that leaves a branch for another branch of the same block goes past the block instead. Every path that runs is among the ones these give,
/// and so are some that never run together. That is sound for the graph's questions, each of which asks whether some path goes from one place to another.
/// </remarks>
internal sealed class ConditionalAlternatives
{
    private readonly List<Block> _blocks;
    private readonly int _exit;

    private ConditionalAlternatives(List<Block> blocks, int exit)
    {
        _blocks = blocks;
        _exit = exit;
    }

    /// <summary>
    /// The alternatives of the instructions of a body, by where each is written. An instruction with no location (the jump that ends a branch of an <c>If</c>)
    /// is in the branches that hold both the instruction before it and the one after it that have one: at the edge of a block, it is outside of it.
    /// </summary>
    /// <param name="instructions">The body, lowered with every branch.</param>
    /// <param name="blocks">The <c>#If</c> blocks of its module.</param>
    public static ConditionalAlternatives? Of(InstructionList instructions, ConditionalCompilationBlocks blocks)
    {
        var items = instructions.Items;
        var located = items.Select(instruction => instruction.Location is { } location ? blocks.BranchesAt(location.Range.Start, _ => true) : null).ToArray();
        var before = new IReadOnlyList<(int Block, int Branch)>[items.Length];
        var after = new IReadOnlyList<(int Block, int Branch)>[items.Length];
        IReadOnlyList<(int Block, int Branch)> last = [];
        for (var offset = 0; offset < items.Length; offset++)
        {
            before[offset] = last;
            last = located[offset] ?? last;
        }

        last = [];
        for (var offset = items.Length - 1; offset >= 0; offset--)
        {
            after[offset] = last;
            last = located[offset] ?? last;
        }

        var members = new Dictionary<int, Dictionary<int, int>>();
        for (var offset = 0; offset < items.Length; offset++)
        {
            var branches = located[offset] ?? [.. before[offset].Intersect(after[offset])];
            foreach (var (block, branch) in branches)
            {
                if (!members.TryGetValue(block, out var offsets))
                {
                    members.Add(block, offsets = []);
                }

                offsets[offset] = branch;
            }
        }

        if (members.Count == 0)
        {
            return null;
        }

        return new([.. members.Values.Select(offsets => new Block(
            offsets,
            [.. offsets.GroupBy(member => member.Value).Select(branch => branch.Min(member => member.Key))],
            offsets.Keys.Max() + 1))], instructions.Items.Length);
    }

    /// <summary>
    /// Where control that leaves <paramref name="from"/> for <paramref name="to"/> can arrive.
    /// </summary>
    /// <param name="from">The offset of the instruction it leaves; -1 for the start of the body.</param>
    /// <param name="to">Where it goes.</param>
    public IEnumerable<int> Arrivals(int from, int to)
    {
        var arrivals = new HashSet<int>();
        var seen = new HashSet<int>();
        var pending = new Stack<int>();
        pending.Push(to);
        while (pending.TryPop(out var node))
        {
            if (!seen.Add(node))
            {
                continue;
            }

            if (node >= _exit || node < 0)
            {
                arrivals.Add(node);
                continue;
            }

            // from one branch of a block into another: the two are never compiled together, so control goes past the block.
            if (_blocks.FirstOrDefault(block => block.Crosses(from, node)) is { } crossed)
            {
                pending.Push(crossed.After);
                continue;
            }

            arrivals.Add(node);

            // into a block at the start of one of its branches: whichever branch is compiled starts there, or none is.
            foreach (var entered in _blocks.Where(block => block.IsEnteredAt(from, node)))
            {
                foreach (var first in entered.Firsts)
                {
                    pending.Push(first);
                }

                pending.Push(entered.After);
            }
        }

        return arrivals;
    }

    /// <summary>
    /// Where control that comes to the run of instructions from <paramref name="first"/> to <paramref name="last"/> from outside it arrives: at
    /// <paramref name="first"/>, or, for each block the run holds the whole of and starts with, at the start of any branch of it, or past it.
    /// </summary>
    /// <param name="first">The offset of the run's first instruction.</param>
    /// <param name="last">The offset of its last instruction.</param>
    public IReadOnlySet<int> EntriesOf(int first, int last)
    {
        var entries = new HashSet<int>();
        var pending = new Stack<int>();
        pending.Push(first);
        while (pending.TryPop(out var node))
        {
            if (!entries.Add(node))
            {
                continue;
            }

            foreach (var held in _blocks.Where(block => block.Firsts.Contains(node) && block.Members.Keys.All(member => member >= first && member <= last)))
            {
                foreach (var start in held.Firsts)
                {
                    pending.Push(start);
                }

                pending.Push(held.After);
            }
        }

        return entries;
    }

    // a block: the branch each of its instructions is in, the first instruction of each branch that has one, and the instruction after the last of them.
    private sealed record class Block(Dictionary<int, int> Members, int[] Firsts, int After)
    {
        public bool Crosses(int from, int to)
            => Members.TryGetValue(from, out var left) && Members.TryGetValue(to, out var arrived) && left != arrived;

        public bool IsEnteredAt(int from, int to) => !Members.ContainsKey(from) && Firsts.Contains(to);
    }
}
