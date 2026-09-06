using RDCore.SDK.Runtime.Shared;

namespace RDCore.SDK.Runtime.Abstract.Execution;

/// <summary>
/// Allocates and frees blocks within an execution session's memory space, and reports allocation
/// and fragmentation statistics.
/// </summary>
/// <remarks>
/// ⚖️<strong>RDCore</strong> provides an implementation of this interface <strong>licensed under GPLv3</strong>.
/// </remarks>
public interface ISessionMemoryAllocator
{
    /// <summary>
    /// Allocates the specified number of bytes in the memory space of this session.
    /// </summary>
    /// <param name="size">The desired size of the allocation.</param>
    /// <param name="address">The start of the allocated address space.</param>
    /// <returns><c>true</c> if the specified number of bytes can be allocated, <c>false</c> otherwise.</returns>
    bool TryAllocate(int size, out MemoryAddress address);

    /// <summary>
    /// Frees the block starting at <paramref name="address"/>.
    /// </summary>
    bool TryDeallocate(MemoryAddress address, out SessionMemoryBlock block);

    /// <summary>
    /// Current allocation and fragmentation statistics for this session's memory space.
    /// </summary>
    SessionMemoryInfo Info { get; }
}
