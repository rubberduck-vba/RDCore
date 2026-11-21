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


    // a block, and the bytes before it that were skipped to align it (SessionMemory.AlignmentFor): those are allocated and freed with the block, so
    // that what a program releases leaves no stray bytes behind, and a smaller block is never placed between two variables that are side by side.
    private readonly record struct Placed(SessionMemoryBlock Block, int Padding);

    private readonly Dictionary<MemoryAddress, Placed> _memoryMap = [];

    private MemoryAddress _currentAddress;
    private readonly MemoryAddress _nextSegmentAddress;

    private SessionMemoryInfo _info;
    public SessionMemoryInfo Info => _info;

    public bool TryAllocate(int size, out MemoryAddress address) => TryAllocate(size, SessionMemory.AlignmentFor(size), out address);

    /// <summary>
    /// Whether a block of <paramref name="size"/> bytes on an <paramref name="alignment"/> boundary can be carved from this segment's unused memory.
    /// </summary>
    /// <param name="size">The size of the block, in bytes.</param>
    /// <param name="alignment">The boundary the block starts on, a power of two.</param>
    internal bool CanCarve(int size, int alignment) => SessionMemory.AlignUp(_currentAddress.Value, alignment) + size <= _nextSegmentAddress.Value;

    /// <summary>
    /// Carves a block of <paramref name="size"/> bytes from this segment's unused memory, at the first <paramref name="alignment"/> boundary.
    /// </summary>
    /// <param name="size">The size of the block, in bytes.</param>
    /// <param name="alignment">The boundary the block starts on, a power of two.</param>
    /// <param name="address">The address of the block.</param>
    /// <returns><see langword="false"/> when the size is not positive, or the block does not fit.</returns>
    internal bool TryAllocate(int size, int alignment, out MemoryAddress address)
    {
        if (size <= 0 || !CanCarve(size, alignment))
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

        var start = (int)SessionMemory.AlignUp(_currentAddress.Value, alignment);
        address = Allocate(new SessionMemoryBlock(new MemoryAddress(start), size), start - _currentAddress.Value);
        _currentAddress = new MemoryAddress(start + size);

        return true;
    }

    /// <summary>
    /// Where the unused memory of this segment begins: the end of the memory handed out so far. The next block carved from it starts there, or at the
    /// first boundary after it that the block is aligned to.
    /// </summary>
    internal MemoryAddress CurrentAddress => _currentAddress;

    // a block carved from this segment's unused memory, and so committed from now on, with the padding before it.
    private MemoryAddress Allocate(SessionMemoryBlock block, int padding)
    {
        _memoryMap[block.Address] = new Placed(block, padding);
        _info = _info
            .WithAllocated(block.Size + padding)
            .WithCommitted(block.Size + padding);

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
    /// <param name="block">The block, where the free memory it is taken from reaches its alignment.</param>
    /// <param name="padding">The free bytes before <paramref name="block"/> that are skipped to align it, allocated with it.</param>
    internal MemoryAddress Reuse(SessionMemoryBlock block, int padding)
    {
        _memoryMap[block.Address] = new Placed(block, padding);
        _info = _info
            .WithAllocated(block.Size + padding)
            .WithFree(-(block.Size + padding));

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
        // block the address falls inside, which is what makes a byte-level read of one possible. The padding before a block is in none.
        if (_memoryMap.TryGetValue(address, out var placed))
        {
            block = placed.Block;
            return true;
        }

        foreach (var candidate in _memoryMap.Values)
        {
            if (candidate.Block.Address.Value <= address.Value && address.Value < candidate.Block.Address.Value + candidate.Block.Size)
            {
                block = candidate.Block;
                return true;
            }
        }

        block = default;
        return false;
    }

    public bool TryDeallocate(MemoryAddress address, out SessionMemoryBlock block) => TryDeallocate(address, out block, out _);

    /// <summary>
    /// Frees the block at <paramref name="address"/>, and the padding before it.
    /// </summary>
    /// <param name="address">The address of the block.</param>
    /// <param name="block">The block freed.</param>
    /// <param name="padding">The bytes before <paramref name="block"/> that aligned it, free with it.</param>
    /// <returns><see langword="false"/> when no block of this segment starts at <paramref name="address"/>.</returns>
    internal bool TryDeallocate(MemoryAddress address, out SessionMemoryBlock block, out int padding)
    {
        if (_memoryMap.Remove(address, out var placed))
        {
            (block, padding) = placed;
            _info = _info
                .WithAllocated(-(block.Size + padding))
                .WithFree(block.Size + padding);

            return true;
        }

        block = default;
        padding = 0;
        return false;
    }
}
