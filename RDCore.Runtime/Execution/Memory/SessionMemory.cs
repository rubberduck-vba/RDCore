using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using System.Diagnostics.CodeAnalysis;

namespace RDCore.Runtime.Execution.Memory;

internal sealed class SessionMemory : ISessionMemoryAllocator
{
    private readonly FreeListManager _freeLists;
    private readonly Stack<SessionMemorySegment> _segments = new(4);
    private readonly List<SessionMemorySegment> _availableSegments = new(4);

    private readonly int _segmentSize;

    public SessionMemory(FreeListManager freeLists, PointerSize pointerSize, int offset = 0)
    {
        PointerSize = pointerSize;

        _freeLists = freeLists;
        _segmentSize = pointerSize == PointerSize.x86 ? SessionMemorySegment.SegmentSize32 : SessionMemorySegment.SegmentSize64;

        CreateNewReservedSegment(new(offset));
    }

    internal IEnumerable<SessionMemorySegment> Segments => _segments.AsEnumerable();
    private bool TryGetAvailableSegment(int size, [NotNullWhen(true)][MaybeNullWhen(false)] out SessionMemorySegment? segment)
    {
        for (var i = 0; i < _availableSegments.Count; i++)
        {
            var avlbSegment = _availableSegments[i];
            if (avlbSegment.Info.AvailableBytes >= size)
            {
                segment = avlbSegment;
                return true;
            }
        }

        segment = null;
        return false;
    }

    private bool TryFindSegment(MemoryAddress address, [NotNullWhen(true)][MaybeNullWhen(false)] out SessionMemorySegment? segment)
    {
        foreach (var prospect in _segments) // stack iterates most recent ones first.. not necessarily last allocated segment.
        {
            if (prospect.Address.Value <= address.Value && prospect.NextSegment.Value > address.Value)
            {
                segment = prospect;
                return true;
            }
        }

        segment = null;
        return false;
    }

    private SessionMemorySegment CreateNewReservedSegment(MemoryAddress address, int? size = default)
    {
        var segment = new SessionMemorySegment(address, Math.Max(size ?? _segmentSize, _segmentSize), PointerSize);
        _segments.Push(segment);
        _availableSegments.Add(segment);

        return segment;
    }

    public PointerSize PointerSize { get; }

    public SessionMemoryInfo Info
    {
        get
        {
            var totalReserved = 0;
            var totalAllocated = 0;
            var totalCommitted = 0;
            var totalFreeList = 0;
            var largestFreeBlock = _freeLists.LargestFreeBlockSize;

            foreach (var segment in _segments)
            {
                totalReserved += segment.Info.ReservedSegmentBytes;
                totalAllocated += segment.Info.AllocatedBytes;
                totalCommitted += segment.Info.CommittedBytes;
                totalFreeList += segment.Info.FreeBytes;
            }

            return new(totalReserved, totalAllocated, totalCommitted, totalFreeList, largestFreeBlock);
        }
    }

    public bool TryAllocate(int size, out MemoryAddress address)
    {
        if (_freeLists.TryGetFreeListBlock(size, out var block, out var segment))
        {
            // free memory fast path
            address = block.Value.Address;
            return true;
        }
        else if (!TryGetAvailableSegment(size, out segment))
        {
            // segment full slow path
            segment = CreateNewReservedSegment(_segments.OrderBy(s => s.Address.Value).Last().NextSegment, size);
        }

        var result = segment.TryAllocate(size, out address);
        if (segment.Info.AvailableBytes < 8)
        {
            _availableSegments.Remove(segment);    
        }

        return result;
    }

    public bool TryDeallocate(MemoryAddress address, out SessionMemoryBlock block)
    {
        // NOTE: deallocation does not check if the segment is left empty; this is intentional.
        if (TryFindSegment(address, out var segment) && segment.TryDeallocate(address, out block))
        {
            // the deallocated block becomes free memory
            _freeLists.Add(block, segment);
            _availableSegments.Add(segment);
            return true;
        }

        block = default;
        return false;
    }
}
