using MediatR;
using OmniSharp.Extensions.JsonRpc;
using RDCore.SDK.Client;

namespace RDCore.SDK.Platform.Protocol;

/// <summary>
/// <see cref="SessionMessageBoxParams"/> as the platform's JSON-RPC handlers and senders know it: the method it is a request of, and what it is answered
/// with.
/// </summary>
[Method(RDCorePlatformProtocol.SessionMessageBox, Direction.ServerToClient)]
public sealed record class SessionMessageBoxRequest : SessionMessageBoxParams, IRequest<MessageBoxResult>;

/// <summary>
/// <see cref="SessionInputBoxParams"/> as the platform's JSON-RPC handlers and senders know it: the method it is a request of, and what it is answered
/// with.
/// </summary>
[Method(RDCorePlatformProtocol.SessionInputBox, Direction.ServerToClient)]
public sealed record class SessionInputBoxRequest : SessionInputBoxParams, IRequest<InputBoxResult>;
