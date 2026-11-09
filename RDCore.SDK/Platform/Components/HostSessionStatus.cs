using MediatR;
using OmniSharp.Extensions.JsonRpc;
using RDCore.SDK.Client;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.SDK.Platform.Components;

/// <summary>
/// Request for <c>rdcore/host/session/status</c>: the language-server side of
/// <see cref="SessionStatusParams"/>, addressed to the component that owns the session.
/// </summary>
/// <remarks>
/// Part of the component protocol (<see cref="RDCoreComponentProtocol"/>), outside the SDK's compatibility promise.
/// </remarks>
[Method(RDCoreComponentProtocol.HostSessionStatus, Direction.ClientToServer)]
public record class HostSessionStatusParams : IRequest, IRequest<SessionStatusResult>;
