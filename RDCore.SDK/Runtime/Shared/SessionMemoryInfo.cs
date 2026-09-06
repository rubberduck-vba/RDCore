namespace RDCore.SDK.Runtime.Shared;

public record struct SessionMemoryInfo(
    int ReservedSegmentBytes, 
    int AllocatedBytes, 
    int CommittedBytes,
    int FreeBytes,
    int LargestFreeBlock)
{
    public readonly int AvailableBytes => ReservedSegmentBytes - AllocatedBytes;
    public readonly int UncommittedBytes => ReservedSegmentBytes - CommittedBytes;
 
    /// <summary>
    /// Increments the <em>allocated bytes</em> by the specified amount.
    /// </summary>
    /// <param name="bytes">The number of bytes to be <strong>added</strong> to the current value.</param>
    /// <returns>A new <em>memory info</em> data structure representing the new value.</returns>
    public SessionMemoryInfo WithAllocated(int bytes) => this with
    {
        AllocatedBytes = AllocatedBytes + bytes,
    };

    /// <summary>
    /// Increments the <em>free bytes</em> by the specified amount.
    /// </summary>
    /// <param name="bytes">The number of bytes to be <strong>added</strong> to the current value.</param>
    /// <returns>A new <em>memory info</em> data structure representing the new value.</returns>
    public SessionMemoryInfo WithFree(int bytes) => this with
    {
        FreeBytes = FreeBytes + bytes,
    };

    /// <summary>
    /// Increments the <em>committed bytes</em> by the specified amount.
    /// </summary>
    /// <param name="bytes">The number of bytes to be <strong>added</strong> to the current value.</param>
    /// <returns>A new <em>memory info</em> data structure representing the new value.</returns>
    public SessionMemoryInfo WithCommitted(int bytes) => this with
    {
        CommittedBytes = CommittedBytes + bytes,
    };
}
