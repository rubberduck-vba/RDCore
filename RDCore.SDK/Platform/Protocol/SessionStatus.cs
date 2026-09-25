using MediatR;
using OmniSharp.Extensions.JsonRpc;
using RDCore.SDK.Client;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.SDK.Platform.Protocol;

/// <summary>
/// Request for <c>rdcore/session/status</c>: a client asks the language server for the state of the
/// platform's runtime session.
/// </summary>
/// <remarks>
/// A client never addresses the environment host directly — it does not know one exists. This is a
/// request of the <em>language server</em>, which services it from whichever component actually owns
/// the session (today, the environment host, over
/// <see cref="RDCorePlatformProtocol.HostSessionStatus"/>).
/// </remarks>
[Method(RDCorePlatformProtocol.SessionStatus, Direction.ClientToServer)]
public record class SessionStatusParams : IRequest, IRequest<SessionStatusResult>
{
    /// <summary>
    /// How long, in milliseconds, the language server may wait for the session to come up before
    /// answering that it has not. A shell that prints a readiness banner wants to wait; a status
    /// panel refreshing on a timer does not. Zero answers immediately with whatever is true now.
    /// </summary>
    public int WaitMilliseconds { get; init; }
}

/// <summary>
/// Request for <c>rdcore/host/session/status</c>: the language-server side of
/// <see cref="SessionStatusParams"/>, addressed to the component that owns the session.
/// </summary>
[Method(RDCorePlatformProtocol.HostSessionStatus, Direction.ClientToServer)]
public record class HostSessionStatusParams : IRequest, IRequest<SessionStatusResult>;

/// <summary>
/// The state of the platform's runtime session. The same result answers both hops of the status
/// request — the language server passes the host's answer through unchanged.
/// </summary>
public record class SessionStatusResult
{
    /// <summary>
    /// Whether a runtime session exists at all. Everything else is meaningless when this is
    /// <c>false</c> — the session is composed during platform bring-up, so a client asking early
    /// (or with no environment host in the platform) sees it unset.
    /// </summary>
    public bool IsComposed { get; init; }

    /// <summary>
    /// The session project's name, or an empty string when the session was composed without one.
    /// </summary>
    public string ProjectName { get; init; } = string.Empty;

    /// <summary>
    /// The number of modules the session's project declares.
    /// </summary>
    public int ModuleCount { get; init; }

    /// <summary>
    /// The number of library references the session was composed with.
    /// </summary>
    public int ReferenceCount { get; init; }

    /// <summary>
    /// The session memory space's allocation and fragmentation statistics, as the session's own
    /// allocator reports them.
    /// </summary>
    public SessionMemoryStatus Memory { get; init; } = new();
}

/// <summary>
/// A transport-friendly projection of <see cref="SessionMemoryInfo"/>.
/// </summary>
/// <remarks>
/// <see cref="SessionMemoryInfo"/> itself is a <c>record struct</c> with computed members, which the
/// JSON-RPC serializer would round-trip only by accident; this carries the same numbers as plain
/// properties, computed ones included, so a client renders them without re-deriving anything.
/// </remarks>
public record class SessionMemoryStatus
{
    /// <summary>The total address space the session's segments reserve.</summary>
    public int ReservedBytes { get; init; }

    /// <summary>Reserved bytes not currently handed out — <c>reserved - allocated</c>.</summary>
    public int AvailableBytes { get; init; }

    /// <summary>Bytes currently handed out to live allocations.</summary>
    public int AllocatedBytes { get; init; }

    /// <summary>Bytes that were allocated and released, and sit on the free lists for reuse.</summary>
    public int FreeBytes { get; init; }

    /// <summary>Bytes backed by committed storage.</summary>
    public int CommittedBytes { get; init; }

    /// <summary>The largest single block the free lists can satisfy without a new segment.</summary>
    public int LargestFreeBlock { get; init; }

    /// <summary>
    /// Projects an allocator's own <see cref="SessionMemoryInfo"/> onto the wire.
    /// </summary>
    /// <param name="info">The allocation statistics to project.</param>
    public static SessionMemoryStatus From(SessionMemoryInfo info) => new()
    {
        ReservedBytes = info.ReservedSegmentBytes,
        AvailableBytes = info.AvailableBytes,
        AllocatedBytes = info.AllocatedBytes,
        FreeBytes = info.FreeBytes,
        CommittedBytes = info.CommittedBytes,
        LargestFreeBlock = info.LargestFreeBlock,
    };
}
