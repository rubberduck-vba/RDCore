using MediatR;
using OmniSharp.Extensions.JsonRpc;
using RDCore.SDK.Client;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.SDK.Platform.Components;

/// <summary>
/// Request <c>rdcore/host/interaction/msgBox</c>: the environment host asks the language server to have a program's <c>MsgBox</c> answered by the client.
/// </summary>
/// <remarks>
/// Part of the component protocol (<see cref="RDCoreComponentProtocol"/>), outside the SDK's compatibility promise.
/// </remarks>
[Method(RDCoreComponentProtocol.HostMessageBox, Direction.ServerToClient)]
public sealed record class HostMessageBoxParams : MessageBoxParams, IRequest<MessageBoxResult>;

/// <summary>
/// Request <c>rdcore/host/interaction/inputBox</c>: the environment host asks the language server to have a program's <c>InputBox</c> answered by the client.
/// </summary>
/// <remarks>
/// Part of the component protocol (<see cref="RDCoreComponentProtocol"/>), outside the SDK's compatibility promise.
/// </remarks>
[Method(RDCoreComponentProtocol.HostInputBox, Direction.ServerToClient)]
public sealed record class HostInputBoxParams : InputBoxParams, IRequest<InputBoxResult>;
