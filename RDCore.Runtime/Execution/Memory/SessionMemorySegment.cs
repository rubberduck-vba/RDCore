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
        if (_currentAddress.Value + size > _nextSegmentAddress.Value)
        {
            // segment is full
            address = default;
            return false;
        }

        address = Allocate(new SessionMemoryBlock(_currentAddress, size));
        Advance(size);

        return true;
    }

    internal MemoryAddress Allocate(SessionMemoryBlock block)
    {
        _memoryMap[block.Address] = block;
        _info = _info
            .WithAllocated(block.Size)
            .WithCommitted(block.Size);

        return block.Address;
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
