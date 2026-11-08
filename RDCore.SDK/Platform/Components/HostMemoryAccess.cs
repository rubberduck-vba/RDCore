using MediatR;
using OmniSharp.Extensions.JsonRpc;
using RDCore.SDK.Client;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.SDK.Platform.Components;

/// <summary>
/// Request for <c>rdcore/host/memory/peek</c>: the language-server side of
/// <see cref="SessionPeekParams"/>, addressed to the component that owns the session.
/// </summary>
/// <remarks>
/// Part of the component protocol (<see cref="RDCoreComponentProtocol"/>), outside the SDK's compatibility promise.
/// </remarks>
[Method(RDCoreComponentProtocol.HostPeek, Direction.ClientToServer)]
public record class HostPeekParams : IRequest, IRequest<PeekResult>
{
    /// <summary>The address to read.</summary>
    public int Address { get; init; }
}

/// <summary>
/// Request for <c>rdcore/host/memory/poke</c>: the language-server side of
/// <see cref="SessionPokeParams"/>, addressed to the component that owns the session.
/// </summary>
/// <remarks>
/// Part of the component protocol (<see cref="RDCoreComponentProtocol"/>), outside the SDK's compatibility promise.
/// </remarks>
[Method(RDCoreComponentProtocol.HostPoke, Direction.ClientToServer)]
public record class HostPokeParams : IRequest, IRequest<PokeResult>
{
    /// <summary>The address to write.</summary>
    public int Address { get; init; }

    /// <summary>The byte to write there.</summary>
    public byte Value { get; init; }
}
