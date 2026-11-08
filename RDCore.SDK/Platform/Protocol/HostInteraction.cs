using MediatR;
using OmniSharp.Extensions.JsonRpc;
using RDCore.SDK.Client;

namespace RDCore.SDK.Platform.Protocol;

/// <summary>
/// Request <c>rdcore/host/interaction/msgBox</c>: the environment host asks the language server to have a program's <c>MsgBox</c> answered by the client.
/// </summary>
[Method(RDCoreComponentProtocol.HostMessageBox, Direction.ServerToClient)]
public sealed record class HostMessageBoxParams : MessageBoxParams, IRequest<MessageBoxResult>;

/// <summary>
/// Request <c>rdcore/host/interaction/inputBox</c>: the environment host asks the language server to have a program's <c>InputBox</c> answered by the client.
/// </summary>
[Method(RDCoreComponentProtocol.HostInputBox, Direction.ServerToClient)]
public sealed record class HostInputBoxParams : InputBoxParams, IRequest<InputBoxResult>;

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
