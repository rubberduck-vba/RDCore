using RDCore.SDK.Runtime.Shared;
using System.Diagnostics.CodeAnalysis;

namespace RDCore.Runtime.Execution.Memory;

internal class FreeListManager
{
    private static readonly int _smallListSize = 8;

    private readonly Dictionary<SessionMemoryBlock, SessionMemorySegment> _segmentMap = [];
    private readonly FreeBlocksList _freeSmallBlocks = new();
    private readonly FreeBlocksList _freeLargeBlocks = new();

    public int LargestFreeBlockSize => Math.Max(_freeLargeBlocks.LargestBlockSize, _freeSmallBlocks.LargestBlockSize);

    public void Add(SessionMemoryBlock block, SessionMemorySegment segment)
    {
        var list = block.Size <= _smallListSize 
            ? _freeSmallBlocks 
            : _freeLargeBlocks;
        
        list.Add(block);
        _segmentMap.Add(block, segment);
    }

    public bool TryGetFreeListBlock(int size, [MaybeNullWhen(false)][NotNullWhen(true)] out SessionMemoryBlock? block, [MaybeNullWhen(false)][NotNullWhen(true)] out SessionMemorySegment? segment)
    {
        var freeList = size <= _smallListSize ? _freeSmallBlocks : _freeLargeBlocks;
        if (freeList.Count > 0 && freeList[^1].Size >= size)
        {
            // free-list is usable, so we use the smallest available block that fits
            for (var i = 0; i < freeList.Count; i++)
            {
                if (freeList[i].Size >= size)
                {
                    block = freeList[i];  // ideal case: no fragmentation

                    freeList.RemoveAt(i); // this block is no longer free
                    _segmentMap.Remove(block.Value, out segment!);

                    if (block.Value.Size > size)
                    {
                        // free block is larger than the size we need; this leaves a fragment block behind
                        var fragment = new SessionMemoryBlock(block.Value.Address + size, block.Value.Size - size);
                        block = new SessionMemoryBlock(block.Value.Address, size);

                        // add the fragment as a new free block, move memory block to small list as needed
                        if (fragment.Size <= _smallListSize)
                        {
                            _freeSmallBlocks.Add(fragment);
                            _segmentMap.Add(fragment, segment);
                        }
                        else
                        {
                            _freeLargeBlocks.Add(fragment);
                            _segmentMap.Add(fragment, segment);
                        }
                        // ...and fragmentation is practically inexistent since we leave no small block in the large free list.
                    }
                    return true;
                }
            }
        }
        block = default;
        segment = default;
        return false;
    }
}
