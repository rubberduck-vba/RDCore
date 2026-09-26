using MediatR;
using OmniSharp.Extensions.JsonRpc;
using RDCore.SDK.Client;

namespace RDCore.SDK.Platform.Protocol;

/// <summary>
/// Request for <c>rdcore/session/memory/peek</c>: a client reads one byte of the runtime session's
/// memory.
/// </summary>
/// <remarks>
/// Direct, byte-level access to a live session's memory space, addressed by nothing but a number.
/// Nothing about the address has to mean anything, and the platform does not try to make it mean
/// anything: it reports the byte there, or that there is nothing there.
/// </remarks>
[Method(RDCorePlatformProtocol.SessionPeek, Direction.ClientToServer)]
public record class PeekSessionParams : IRequest, IRequest<PeekSessionResult>
{
    /// <summary>The address to read.</summary>
    public int Address { get; init; }
}

/// <summary>
/// Request for <c>rdcore/host/memory/peek</c>: the language-server side of
/// <see cref="PeekSessionParams"/>, addressed to the component that owns the session.
/// </summary>
[Method(RDCorePlatformProtocol.HostPeek, Direction.ClientToServer)]
public record class HostPeekParams : IRequest, IRequest<PeekSessionResult>
{
    /// <summary>The address to read.</summary>
    public int Address { get; init; }
}

/// <summary>
/// What was at the address.
/// </summary>
public record class PeekSessionResult
{
    /// <summary>The address that was read, echoed back.</summary>
    public int Address { get; init; }

    /// <summary>
    /// Whether anything is allocated there. <c>false</c> is not an error: most of an address space is
    /// nothing.
    /// </summary>
    public bool IsAllocated { get; init; }

    /// <summary>The byte at the address; <c>0</c> when nothing is allocated there.</summary>
    public byte Value { get; init; }
}

/// <summary>
/// Request for <c>rdcore/session/memory/poke</c>: a client writes one byte of the runtime session's
/// memory.
/// </summary>
/// <remarks>
/// Unchecked, and deliberately so. Writing a byte into the middle of a live variable changes that
/// variable to whatever the new bytes spell — an ordinary, valid value of its declared type, or a
/// nonsensical one. Nothing here prevents that; being able to do it is the point.
/// </remarks>
[Method(RDCorePlatformProtocol.SessionPoke, Direction.ClientToServer)]
public record class PokeSessionParams : IRequest, IRequest<PokeSessionResult>
{
    /// <summary>The address to write.</summary>
    public int Address { get; init; }

    /// <summary>The byte to write there.</summary>
    public byte Value { get; init; }
}

/// <summary>
/// Request for <c>rdcore/host/memory/poke</c>: the language-server side of
/// <see cref="PokeSessionParams"/>, addressed to the component that owns the session.
/// </summary>
[Method(RDCorePlatformProtocol.HostPoke, Direction.ClientToServer)]
public record class HostPokeParams : IRequest, IRequest<PokeSessionResult>
{
    /// <summary>The address to write.</summary>
    public int Address { get; init; }

    /// <summary>The byte to write there.</summary>
    public byte Value { get; init; }
}

/// <summary>
/// Whether the byte went in.
/// </summary>
public record class PokeSessionResult
{
    /// <summary>The address that was written, echoed back.</summary>
    public int Address { get; init; }

    /// <summary>
    /// Whether the byte was written. <c>false</c> means nothing is allocated at that address, or what
    /// is cannot be expressed as bytes — an object reference, an array, a user-defined type.
    /// </summary>
    public bool IsWritten { get; init; }
}
