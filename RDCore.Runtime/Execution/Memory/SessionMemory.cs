using RDCore.SDK.Model.Types;
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

    /// <summary>
    /// Where a session's address space begins: above the first 64 KiB, which Windows never maps either, so that no variable's address is 0 - what a
    /// program compares a pointer with to say it points nowhere (<c>StrPtr(vbNullString)</c>, <c>ObjPtr(Nothing)</c>) - nor a small number.
    /// </summary>
    public const int FirstAddress = 0x10000;

    /// <summary>
    /// The boundary a block of <paramref name="size"/> bytes starts on: the largest power of two that divides the size, up to
    /// <see cref="VBUserDefinedTypeLayout.MaxAlignment"/>.
    /// </summary>
    /// <remarks>
    /// A value of an intrinsic type is as wide as its natural alignment (a <c>Long</c> is four bytes on a 4-byte boundary, a <c>Variant</c> 24 bytes on
    /// an 8-byte one), and a record or an array is a whole number of the alignment of its widest member, so a block aligned this way is aligned for what
    /// it holds. MS-VBA lays out the variables of a module the same way, in declaration order (observed in Excel, 64-bit).
    /// </remarks>
    /// <param name="size">The size of the block, in bytes: positive.</param>
    internal static int AlignmentFor(int size) => Math.Min(VBUserDefinedTypeLayout.MaxAlignment, size & -size);

    /// <summary>
    /// The first address at or after <paramref name="address"/> on an <paramref name="alignment"/> boundary.
    /// </summary>
    /// <param name="address">The address to align.</param>
    /// <param name="alignment">A power of two.</param>
    internal static long AlignUp(long address, int alignment) => (address + alignment - 1) & -alignment;

    public SessionMemory(FreeListManager freeLists, PointerSize pointerSize, int offset = 0)
    {
        PointerSize = pointerSize;

        _freeLists = freeLists;
        _segmentSize = pointerSize == PointerSize.x86 ? SessionMemorySegment.SegmentSize32 : SessionMemorySegment.SegmentSize64;

        CreateNewReservedSegment(new(offset));
    }

    internal IEnumerable<SessionMemorySegment> Segments => _segments.AsEnumerable();
    private bool TryGetAvailableSegment(int size, int alignment, [NotNullWhen(true)][MaybeNullWhen(false)] out SessionMemorySegment? segment)
    {
        for (var i = 0; i < _availableSegments.Count; i++)
        {
            // room to carve a new block from, which is what a segment is available for: the free blocks are the free list's, and were offered first.
            var avlbSegment = _availableSegments[i];
            if (avlbSegment.CanCarve(size, alignment))
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
        // a whole number of the widest alignment, so that the segment after it starts on that boundary too, and a block carved at the start of one is
        // never padded: a segment reserved for a block bigger than the usual size is as big as that block, rounded up to the boundary.
        var reserved = (int)Math.Min(int.MaxValue, AlignUp(Math.Max(size ?? _segmentSize, _segmentSize), VBUserDefinedTypeLayout.MaxAlignment));
        var segment = new SessionMemorySegment(address, reserved, PointerSize);
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
        if (size <= 0)
        {
            // reject before ever consulting the free list: FreeListManager.TryGetFreeListBlock's own
            // `freeList[i].Size >= size` is trivially satisfied by any free block when size is 0, and
            // splitting a block for a 0-byte request produces a "fragment" identical in address AND
            // size to the original (size - 0 == size) - the free list would keep re-offering that same
            // address to every later caller while the 0-byte "allocation" is still logically live. See
            // SessionMemorySegment.TryAllocate's own guard for the equivalent bump-pointer-path reason.
            address = default;
            return false;
        }
        var alignment = AlignmentFor(size);
        if (_freeLists.TryGetFreeListBlock(size, alignment, out var block, out var padding, out var segment))
        {
            // free memory fast path — the block must be re-registered with its segment's own map, or
            // a later TryDeallocate on this exact address finds nothing to remove (segment.TryDeallocate
            // checks _memoryMap) and silently no-ops: the block never returns to the free list a second
            // time, so it survives exactly one reuse cycle before leaking permanently.
            address = block.Value.Address;
            segment.Reuse(block.Value, padding);
            return true;
        }
        else if (!TryGetAvailableSegment(size, alignment, out segment))
        {
            // segment full slow path
            segment = CreateNewReservedSegment(_segments.OrderBy(s => s.Address.Value).Last().NextSegment, size);
        }

        var result = segment.TryAllocate(size, alignment, out address);
        if (segment.Info.UncommittedBytes < 8)
        {
            _availableSegments.Remove(segment);
        }

        return result;
    }

    public int Reclaim()
    {
        var reclaimed = 0;
        foreach (var segment in _segments)
        {
            // the free block that ends where the segment's unused memory begins is unused memory itself; then so is the one before it, if it ended there.
            var gaveBack = false;
            while (_freeLists.TryTakeEndingAt(segment, segment.CurrentAddress, out var block))
            {
                segment.Retract(block);
                reclaimed += block.Size;
                gaveBack = true;
            }

            if (gaveBack && !_availableSegments.Contains(segment))
            {
                _availableSegments.Add(segment);
            }
        }

        return reclaimed;
    }

    public bool TryFindBlock(MemoryAddress address, out SessionMemoryBlock block)
    {
        if (TryFindSegment(address, out var segment))
        {
            return segment.TryFindBlock(address, out block);
        }

        block = default;
        return false;
    }

    public bool TryDeallocate(MemoryAddress address, out SessionMemoryBlock block)
    {
        // NOTE: deallocation does not check if the segment is left empty; this is intentional.
        if (TryFindSegment(address, out var segment) && segment.TryDeallocate(address, out block, out var padding))
        {
            // the deallocated block becomes free memory, with the padding before it that aligned it
            _freeLists.Add(new SessionMemoryBlock(new MemoryAddress(block.Address.Value - padding), block.Size + padding), segment);
            return true;
        }

        block = default;
        return false;
    }
}
