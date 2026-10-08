using RDCore.SDK.Runtime.Shared;
using System.Diagnostics.CodeAnalysis;

namespace RDCore.Runtime.Execution.Memory;

/// <summary>
/// The free memory of a session's address space: what has been allocated and released, available for
/// reuse before a new segment has to be reserved.
/// </summary>
/// <remarks>
/// A thin face over a single coalescing, address-ordered <see cref="FreeBlocksList"/>. It used to keep
/// two lists partitioned by block size, plus a dictionary mapping each free block to its segment —
/// three structures that had to agree with each other and could not, because merging two adjacent
/// free blocks changes the very key the dictionary was keyed by. Coalescing gets the fragmentation
/// result the size partition was aiming at, and one structure cannot disagree with itself.
/// </remarks>
internal class FreeListManager
{
    private readonly FreeBlocksList _free = new();

    /// <summary>
    /// The largest single allocation the free list can satisfy without reserving a new segment.
    /// </summary>
    public int LargestFreeBlockSize => _free.LargestBlockSize;

    /// <summary>
    /// Returns a released block to the free list.
    /// </summary>
    /// <param name="block">The block being released.</param>
    /// <param name="segment">The segment it was reserved from.</param>
    public void Add(SessionMemoryBlock block, SessionMemorySegment segment) => _free.Add(block, segment);

    /// <summary>
    /// Takes the free block of <paramref name="segment"/> that ends at <paramref name="end"/>, which is how the free memory at the end of a segment is found.
    /// </summary>
    /// <param name="segment">The segment the block belongs to.</param>
    /// <param name="end">The address just after the block.</param>
    /// <param name="block">The free block.</param>
    /// <returns><c>false</c> if no free block of the segment ends there.</returns>
    public bool TryTakeEndingAt(SessionMemorySegment segment, MemoryAddress end, out SessionMemoryBlock block)
        => _free.TryTakeEndingAt(segment, end, out block);

    /// <summary>
    /// Takes the smallest free block that fits <paramref name="size"/>, splitting it if it is larger.
    /// </summary>
    /// <param name="size">The number of bytes wanted.</param>
    /// <param name="block">The block to allocate, exactly <paramref name="size"/> bytes long.</param>
    /// <param name="segment">The segment it belongs to.</param>
    /// <returns><c>false</c> if no free block is big enough.</returns>
    public bool TryGetFreeListBlock(
        int size,
        [MaybeNullWhen(false)][NotNullWhen(true)] out SessionMemoryBlock? block,
        [MaybeNullWhen(false)][NotNullWhen(true)] out SessionMemorySegment? segment)
    {
        if (_free.TryTakeSmallestFit(size, out var found, out segment))
        {
            block = found;
            return true;
        }

        block = null;
        return false;
    }
}
