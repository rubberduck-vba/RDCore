using MediatR;
using OmniSharp.Extensions.JsonRpc;
using RDCore.SDK.Client;

namespace RDCore.SDK.Platform.Protocol;

/// <summary>
/// <see cref="SessionDiscardParams"/> as the platform's JSON-RPC handlers and senders know it: the method it is a request of, and what it is answered with.
/// </summary>
[Method(RDCorePlatformProtocol.SessionDiscard, Direction.ClientToServer)]
public sealed record class SessionDiscardRequest : SessionDiscardParams, IRequest, IRequest<DiscardResult>;
