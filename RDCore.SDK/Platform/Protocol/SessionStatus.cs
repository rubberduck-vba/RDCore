using MediatR;
using OmniSharp.Extensions.JsonRpc;
using RDCore.SDK.Client;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.SDK.Platform.Protocol;

/// <summary>
/// <see cref="SessionStatusParams"/> as the platform's JSON-RPC handlers and senders know it: the method it is a request of, and what it is answered with.
/// </summary>
[Method(RDCorePlatformProtocol.SessionStatus, Direction.ClientToServer)]
public sealed record class SessionStatusRequest : SessionStatusParams, IRequest, IRequest<SessionStatusResult>;

/// <summary>
/// Projects a session allocator's statistics onto the wire.
/// </summary>
public static class SessionMemoryStatusExtensions
{
    /// <summary>
    /// The statistics of <paramref name="info"/>, as a <see cref="SessionMemoryStatus"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="SessionMemoryInfo"/> itself is a <c>record struct</c> with computed members, which the JSON-RPC serializer would round-trip only by
    /// accident; the status carries the same numbers as plain properties, computed ones included.
    /// </remarks>
    /// <param name="info">The allocation statistics to project.</param>
    public static SessionMemoryStatus ToStatus(this SessionMemoryInfo info) => new()
    {
        ReservedBytes = info.ReservedSegmentBytes,
        AvailableBytes = info.AvailableBytes,
        AllocatedBytes = info.AllocatedBytes,
        FreeBytes = info.FreeBytes,
        CommittedBytes = info.CommittedBytes,
        LargestFreeBlock = info.LargestFreeBlock,
    };
}
