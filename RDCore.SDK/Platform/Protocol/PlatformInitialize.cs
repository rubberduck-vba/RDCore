using MediatR;
using OmniSharp.Extensions.JsonRpc;
using RDCore.SDK.Client;
using RDCore.SDK.Client.Connection;

namespace RDCore.SDK.Platform.Protocol;

/// <summary>
/// <see cref="PlatformInitializeParams"/> as the platform's JSON-RPC handlers and senders know it: the method it is a request of, and what it is answered with.
/// Sent by <see cref="ChildConnection"/> once LSP's <c>initialize</c> is answered.
/// </summary>
[Method(RDCorePlatformProtocol.PlatformInitialize, Direction.ClientToServer)]
public sealed record class PlatformInitializeRequest : PlatformInitializeParams, IRequest, IRequest<PlatformInitializeResult>;
