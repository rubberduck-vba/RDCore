using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Runtime.Execution.Memory;

/// <summary>
/// Represents a reserved segment of contiguous managed memory.
/// </summary>
/// <param name="Address">The start address.</param>
/// <param name="PointerSize">The size of an object pointer, in <strong>bytes</strong>.</param>
internal record class SessionMemorySegment : ISessionMemoryAllocator
{
    public static readonly int SegmentSize32 = 2048;
    public static readonly int SegmentSize64 = 4096;

    public SessionMemorySegment(MemoryAddress address, int size, PointerSize pointerSize)
    {
        Address = address;
        Size = size;
        PointerSize = pointerSize;

        _currentAddress = address;
        _nextSegmentAddress = address + size;

        _info = new(size, 0, 0, 0, 0);
    }
    public MemoryAddress Address { get; }
    public MemoryAddress NextSegment => _nextSegmentAddress;

    public int Size { get; }
    public PointerSize PointerSize { get; }


    private readonly Dictionary<MemoryAddress, SessionMemoryBlock> _memoryMap = [];

    private MemoryAddress _currentAddress;
    private readonly MemoryAddress _nextSegmentAddress;
    private MemoryAddress Advance(int size) => _currentAddress += size;

    private SessionMemoryInfo _info;
    public SessionMemoryInfo Info => _info;

    public bool TryAllocate(int size, out MemoryAddress address)
    {
        if (size <= 0 || _currentAddress.Value + size > _nextSegmentAddress.Value)
        {
            // there is no such thing as a 0-byte allocation with a real, distinct address: advancing
            // the bump pointer by 0 hands the same address to the next caller too, and advancing it by
            // anything else would mean the allocation wasn't actually 0 bytes. A negative size (an
            // unchecked overflow upstream, e.g. VBArrayValue.Size's unchecked multiply) would rewind
            // the pointer backward over live allocations, and the later `new byte[size]` a byte-backed
            // allocation performs would throw out of a Try*-named method that must never throw. Reject
            // both the same way a full segment is rejected: return false, move nothing. A value whose
            // declared size is 0 (Nothing, Null, Empty, an uninitialized array, a UDT with no
            // resolvable fields) is a static/global symbol with no session storage of its own — it must
            // never reach TryAllocate at all.
            address = default;
            return false;
        }

        address = Allocate(new SessionMemoryBlock(_currentAddress, size));
        Advance(size);

        return true;
    }

    /// <summary>
    /// Where the next block carved from this segment's unused memory starts: the end of the memory handed out so far.
    /// </summary>
    internal MemoryAddress CurrentAddress => _currentAddress;

    /// <summary>
    /// Records a block that was carved from this segment's unused memory, and so is committed from now on.
    /// </summary>
    internal MemoryAddress Allocate(SessionMemoryBlock block)
    {
        _memoryMap[block.Address] = block;
        _info = _info
            .WithAllocated(block.Size)
            .WithCommitted(block.Size);

        return block.Address;
    }

    /// <summary>
    /// Records a block that was free and is allocated again.
    /// </summary>
    /// <remarks>
    /// It was committed already, when it was first carved, and was counted free when it was released: it is neither committed a second time nor
    /// free any longer. Counting it as <see cref="Allocate"/> counts a new block made the committed bytes and the free bytes grow with every reuse,
    /// for a program that does nothing but allocate a variable and release it.
    /// </remarks>
    internal MemoryAddress Reuse(SessionMemoryBlock block)
    {
        _memoryMap[block.Address] = block;
        _info = _info
            .WithAllocated(block.Size)
            .WithFree(-block.Size);

        return block.Address;
    }

    /// <summary>
    /// Takes back the free block that ends where this segment's unused memory begins, which is unused memory again.
    /// </summary>
    /// <param name="block">A free block of this segment that ends at <see cref="CurrentAddress"/>.</param>
    internal void Retract(SessionMemoryBlock block)
    {
        _currentAddress = block.Address;
        _info = _info
            .WithFree(-block.Size)
            .WithCommitted(-block.Size);
    }

    /// <summary>
    /// A segment does not know its free blocks, which the session's free list keeps: only the session reclaims.
    /// </summary>
    public int Reclaim() => 0;

    public bool TryFindBlock(MemoryAddress address, out SessionMemoryBlock block)
    {
        // an exact hit is the common case (a variable read by its own address); otherwise scan for the
        // block the address falls inside, which is what makes a byte-level read of one possible.
        if (_memoryMap.TryGetValue(address, out block))
        {
            return true;
        }

        foreach (var candidate in _memoryMap.Values)
        {
            if (candidate.Address.Value <= address.Value && address.Value < candidate.Address.Value + candidate.Size)
            {
                block = candidate;
                return true;
            }
        }

        block = default;
        return false;
    }

    public bool TryDeallocate(MemoryAddress address, out SessionMemoryBlock block)
    {
        if (_memoryMap.Remove(address, out block))
        {
            _info = _info
                .WithAllocated(-block.Size)
                .WithFree(block.Size);

            return true;
        }
        return false;
    }
}
