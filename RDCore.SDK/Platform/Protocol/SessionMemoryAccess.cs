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
/// <see cref="SessionPokeParams"/> as the platform's JSON-RPC handlers and senders know it: the method it is a request of, and what it is answered with.
/// </summary>
[Method(RDCorePlatformProtocol.SessionPoke, Direction.ClientToServer)]
public sealed record class SessionPokeRequest : SessionPokeParams, IRequest, IRequest<PokeResult>;
