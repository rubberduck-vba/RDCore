using MediatR;
using OmniSharp.Extensions.JsonRpc;
using RDCore.SDK.Client;

namespace RDCore.SDK.Platform.Protocol;

/// <summary>
/// <see cref="SessionPeekParams"/> as the platform's JSON-RPC handlers and senders know it: the method it is a request of, and what it is answered with.
/// </summary>
[Method(RDCorePlatformProtocol.SessionPeek, Direction.ClientToServer)]
public sealed record class SessionPeekRequest : SessionPeekParams, IRequest, IRequest<PeekResult>;

/// <summary>
/// Request for <c>rdcore/host/memory/peek</c>: the language-server side of
/// <see cref="SessionPeekParams"/>, addressed to the component that owns the session.
/// </summary>
[Method(RDCoreComponentProtocol.HostPeek, Direction.ClientToServer)]
public record class HostPeekParams : IRequest, IRequest<PeekResult>
{
    /// <summary>The address to read.</summary>
    public int Address { get; init; }
}

/// <summary>
/// <see cref="SessionPokeParams"/> as the platform's JSON-RPC handlers and senders know it: the method it is a request of, and what it is answered with.
/// </summary>
[Method(RDCorePlatformProtocol.SessionPoke, Direction.ClientToServer)]
public sealed record class SessionPokeRequest : SessionPokeParams, IRequest, IRequest<PokeResult>;

/// <summary>
/// Request for <c>rdcore/host/memory/poke</c>: the language-server side of
/// <see cref="SessionPokeParams"/>, addressed to the component that owns the session.
/// </summary>
[Method(RDCoreComponentProtocol.HostPoke, Direction.ClientToServer)]
public record class HostPokeParams : IRequest, IRequest<PokeResult>
{
    /// <summary>The address to write.</summary>
    public int Address { get; init; }

    /// <summary>The byte to write there.</summary>
    public byte Value { get; init; }
}
