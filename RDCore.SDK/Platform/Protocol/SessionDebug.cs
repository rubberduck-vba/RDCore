using MediatR;
using OmniSharp.Extensions.JsonRpc;
using RDCore.SDK.Client;

namespace RDCore.SDK.Platform.Protocol;

// the session/debug/* requests a client sends, as the platform's JSON-RPC handlers and senders know them: the method each is a request of, and what it is
// answered with. Each is its Protocol DTO and nothing more, and is written as it.

/// <summary><see cref="SessionDebugResumeParams"/> as a request of <c>rdcore/session/debug/resume</c>.</summary>
[Method(RDCorePlatformProtocol.SessionDebugResume, Direction.ClientToServer)]
public sealed record class SessionDebugResumeRequest : SessionDebugResumeParams, IRequest, IRequest<ExecuteResult>;

/// <summary><see cref="SessionDebugPauseParams"/> as a request of <c>rdcore/session/debug/pause</c>.</summary>
[Method(RDCorePlatformProtocol.SessionDebugPause, Direction.ClientToServer)]
public sealed record class SessionDebugPauseRequest : SessionDebugPauseParams, IRequest, IRequest<DebugAck>;

/// <summary><see cref="SessionDebugGotoParams"/> as a request of <c>rdcore/session/debug/goto</c>.</summary>
[Method(RDCorePlatformProtocol.SessionDebugGoto, Direction.ClientToServer)]
public sealed record class SessionDebugGotoRequest : SessionDebugGotoParams, IRequest, IRequest<DebugGotoResult>;

/// <summary><see cref="SessionDebugEditStatusParams"/> as a request of <c>rdcore/session/debug/editStatus</c>.</summary>
[Method(RDCorePlatformProtocol.SessionDebugEditStatus, Direction.ClientToServer)]
public sealed record class SessionDebugEditStatusRequest : SessionDebugEditStatusParams, IRequest, IRequest<DebugEditStatusResult>;

/// <summary><see cref="SessionDebugTerminateParams"/> as a request of <c>rdcore/session/debug/terminate</c>.</summary>
[Method(RDCorePlatformProtocol.SessionDebugTerminate, Direction.ClientToServer)]
public sealed record class SessionDebugTerminateRequest : SessionDebugTerminateParams, IRequest, IRequest<DebugAck>;

/// <summary><see cref="SessionDebugBreakpointsParams"/> as a request of <c>rdcore/session/debug/breakpoints</c>.</summary>
[Method(RDCorePlatformProtocol.SessionDebugBreakpoints, Direction.ClientToServer)]
public sealed record class SessionDebugBreakpointsRequest : SessionDebugBreakpointsParams, IRequest, IRequest<DebugBreakpointsResult>;

/// <summary><see cref="SessionDebugStackParams"/> as a request of <c>rdcore/session/debug/stack</c>.</summary>
[Method(RDCorePlatformProtocol.SessionDebugStack, Direction.ClientToServer)]
public sealed record class SessionDebugStackRequest : SessionDebugStackParams, IRequest, IRequest<DebugStackResult>;

/// <summary><see cref="SessionDebugVariablesParams"/> as a request of <c>rdcore/session/debug/variables</c>.</summary>
[Method(RDCorePlatformProtocol.SessionDebugVariables, Direction.ClientToServer)]
public sealed record class SessionDebugVariablesRequest : SessionDebugVariablesParams, IRequest, IRequest<DebugVariablesResult>;

/// <summary><see cref="SessionDebugEvaluateParams"/> as a request of <c>rdcore/session/debug/evaluate</c>.</summary>
[Method(RDCorePlatformProtocol.SessionDebugEvaluate, Direction.ClientToServer)]
public sealed record class SessionDebugEvaluateRequest : SessionDebugEvaluateParams, IRequest, IRequest<DebugEvaluateResult>;

/// <summary><see cref="SessionDebugExecuteParams"/> as a request of <c>rdcore/session/debug/execute</c>.</summary>
[Method(RDCorePlatformProtocol.SessionDebugExecute, Direction.ClientToServer)]
public sealed record class SessionDebugExecuteRequest : SessionDebugExecuteParams, IRequest, IRequest<DebugEvaluateResult>;
