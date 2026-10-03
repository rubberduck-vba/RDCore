using RDCore.SDK.Runtime.Shared;
using System.Diagnostics.CodeAnalysis;

namespace RDCore.Runtime.Execution.Memory;

/// <summary>
/// A free block and the segment it was reserved from.
/// </summary>
/// <remarks>
/// The segment travels with the block rather than in a lookup beside the list. It has to: the list
/// merges adjacent blocks, and a merge changes a block's identity — a separate map keyed by block
/// cannot be kept in step with that, and the one this replaced was not. It accumulated keys for
/// blocks that had been merged away and lost the keys for the blocks that replaced them, until an
/// allocation eventually threw on a stale key.
/// </remarks>
/// <param name="Block">The free block.</param>
/// <param name="Segment">The segment it belongs to.</param>
internal readonly record struct FreeBlock(SessionMemoryBlock Block, SessionMemorySegment Segment);

/// <summary>
/// The free blocks of a session's memory space, in address order, coalesced.
/// </summary>
/// <remarks>
/// Address order is not a presentation choice: two free blocks can only be merged when one ends
/// exactly where the other begins, so a structure that cannot find a block's address neighbours
/// cannot coalesce at all. Selection is best-fit — the smallest block that satisfies the request —
/// which is what keeps a large block from being split to serve a small one, the goal the
/// size-partitioned lists this replaced were reaching for less directly.
/// </remarks>
internal sealed class FreeBlocksList
{
    private readonly List<FreeBlock> _blocks = new(32);

    /// <summary>How many free blocks there are.</summary>
    public int Count => _blocks.Count;

    /// <summary>
    /// The largest single request the free list can satisfy without a new segment.
    /// </summary>
    /// <remarks>
    /// Computed, not tracked. A maximum maintained incrementally has to be lowered on every removal
    /// and every merge as well as raised on every add; the one this replaced was only ever raised, so
    /// it over-reported for the rest of the session as soon as any block was taken.
    /// </remarks>
    public int LargestBlockSize => _blocks.Count == 0 ? 0 : _blocks.Max(entry => entry.Block.Size);

    /// <summary>
    /// Returns <paramref name="block"/> to the free list, merging it with either address neighbour
    /// from the same segment.
    /// </summary>
    /// <param name="block">The block being freed.</param>
    /// <param name="segment">The segment it was reserved from.</param>
    /// <returns>
    /// <c>false</c> if that address is already free — a double free. Ignored rather than merged,
    /// because merging it would produce a free block twice the size of the real one and hand the
    /// overlap out to two callers.
    /// </returns>
    public bool Add(SessionMemoryBlock block, SessionMemorySegment segment)
    {
        var index = _blocks.FindIndex(entry => entry.Block.Address.Value > block.Address.Value);
        if (index < 0)
        {
            index = _blocks.Count;
        }

        if (index > 0 && _blocks[index - 1].Block.Address.Value + _blocks[index - 1].Block.Size > block.Address.Value)
        {
            // the predecessor already covers this address: either the same block freed twice, or an
            // overlap that should never have been handed out.
            return false;
        }

        var merged = block;

        // ...with the predecessor, when it ends exactly where this one begins.
        if (index > 0 && ReferenceEquals(_blocks[index - 1].Segment, segment)
            && _blocks[index - 1].Block.Address.Value + _blocks[index - 1].Block.Size == merged.Address.Value)
        {
            merged = new SessionMemoryBlock(_blocks[index - 1].Block.Address, _blocks[index - 1].Block.Size + merged.Size);
            index--;
            _blocks.RemoveAt(index);
        }

        // ...and with the successor, when this one ends exactly where it begins.
        if (index < _blocks.Count && ReferenceEquals(_blocks[index].Segment, segment)
            && merged.Address.Value + merged.Size == _blocks[index].Block.Address.Value)
        {
            merged = new SessionMemoryBlock(merged.Address, merged.Size + _blocks[index].Block.Size);
            _blocks.RemoveAt(index);
        }

        _blocks.Insert(index, new FreeBlock(merged, segment));
        return true;
    }

    /// <summary>
    /// Takes the smallest free block that satisfies <paramref name="size"/>, returning whatever is
    /// left over to the free list.
    /// </summary>
    /// <param name="size">The number of bytes wanted; must be positive.</param>
    /// <param name="block">The block handed out, exactly <paramref name="size"/> bytes long.</param>
    /// <param name="segment">The segment it belongs to.</param>
    /// <returns><c>false</c> if no free block is big enough.</returns>
    public bool TryTakeSmallestFit(int size, [MaybeNullWhen(false)][NotNullWhen(true)] out SessionMemoryBlock block, [MaybeNullWhen(false)][NotNullWhen(true)] out SessionMemorySegment? segment)
    {
        var best = -1;
        for (var i = 0; i < _blocks.Count; i++)
        {
            if (_blocks[i].Block.Size >= size && (best < 0 || _blocks[i].Block.Size < _blocks[best].Block.Size))
            {
                best = i;
            }
        }

        if (best < 0)
        {
            block = default;
            segment = null;
            return false;
        }

        var found = _blocks[best];
        _blocks.RemoveAt(best);

        block = new SessionMemoryBlock(found.Block.Address, size);
        segment = found.Segment;

        if (found.Block.Size > size)
        {
            // the remainder goes back as a free block of its own, which a later free of its neighbour
            // merges back into a whole.
            Add(new SessionMemoryBlock(found.Block.Address + size, found.Block.Size - size), found.Segment);
        }

        return true;
    }
}
