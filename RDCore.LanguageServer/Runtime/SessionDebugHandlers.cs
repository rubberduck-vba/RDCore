using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.JsonRpc.Server;
using RDCore.LanguageServer.Debugging;
using RDCore.SDK.Client;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server.Services;

namespace RDCore.LanguageServer.Runtime;

/// <summary>
/// Handles <c>rdcore/session/debug/resume</c>: relays a client's request to go on with the program it ran under a debugger to the component that owns the runtime
/// session, and answers what that answers - where the program waits next, or how it ended.
/// </summary>
internal sealed class SessionDebugResumeHandler(IProgramDebugService debugging, IPlatformClientCapabilitiesService clientCapabilities)
    : RDCoreRequestHandler<SessionDebugResumeParams, ExecuteSessionResult>
{
    protected override Task<ExecuteSessionResult> HandleAsync(SessionDebugResumeParams request, CancellationToken token)
    {
        SessionDebugRelay.RequireCapability(clientCapabilities);
        return debugging.ResumeAsync(request.Step, token);
    }
}

/// <summary>
/// Handles <c>rdcore/session/debug/pause</c>: relays a client's request to stop the program that is running.
/// </summary>
internal sealed class SessionDebugPauseHandler(IProgramDebugService debugging, IPlatformClientCapabilitiesService clientCapabilities)
    : RDCoreRequestHandler<SessionDebugPauseParams, HostDebugAck>
{
    protected override Task<HostDebugAck> HandleAsync(SessionDebugPauseParams request, CancellationToken token)
    {
        SessionDebugRelay.RequireCapability(clientCapabilities);
        return debugging.PauseAsync(token);
    }
}

/// <summary>
/// Handles <c>rdcore/session/debug/goto</c>: relays a client's request to move the point the program that waits goes on from.
/// </summary>
internal sealed class SessionDebugGotoHandler(IProgramDebugService debugging, IPlatformClientCapabilitiesService clientCapabilities)
    : RDCoreRequestHandler<SessionDebugGotoParams, HostDebugGotoResult>
{
    protected override Task<HostDebugGotoResult> HandleAsync(SessionDebugGotoParams request, CancellationToken token)
    {
        SessionDebugRelay.RequireCapability(clientCapabilities);
        return debugging.GotoAsync(request.Line, request.Label, token);
    }
}

/// <summary>
/// Handles <c>rdcore/session/debug/terminate</c>: relays a client's request to end the program that is running or waits.
/// </summary>
internal sealed class SessionDebugTerminateHandler(IProgramDebugService debugging, IPlatformClientCapabilitiesService clientCapabilities)
    : RDCoreRequestHandler<SessionDebugTerminateParams, HostDebugAck>
{
    protected override Task<HostDebugAck> HandleAsync(SessionDebugTerminateParams request, CancellationToken token)
    {
        SessionDebugRelay.RequireCapability(clientCapabilities);
        return debugging.TerminateAsync(request.Wipe, token);
    }
}

/// <summary>
/// Handles <c>rdcore/session/debug/breakpoints</c>: relays the lines of a module a client wants the program to wait at.
/// </summary>
internal sealed class SessionDebugBreakpointsHandler(IProgramDebugService debugging, IPlatformClientCapabilitiesService clientCapabilities)
    : RDCoreRequestHandler<SessionDebugBreakpointsParams, HostDebugBreakpointsResult>
{
    protected override Task<HostDebugBreakpointsResult> HandleAsync(SessionDebugBreakpointsParams request, CancellationToken token)
    {
        SessionDebugRelay.RequireCapability(clientCapabilities);
        return debugging.SetBreakpointsAsync(request.ModuleName, request.Lines, token);
    }
}

/// <summary>
/// What the debugging relays share: the capability they are gated on.
/// </summary>
internal static class SessionDebugRelay
{
    /// <summary>JSON-RPC 2.0 "Invalid Request".</summary>
    private const int InvalidRequestCode = -32600;

    /// <summary>
    /// Refuses the request unless the client advertised that it debugs programs.
    /// </summary>
    /// <param name="clientCapabilities">What the connected client asked this server for.</param>
    /// <exception cref="RpcErrorException">The client never advertised the capability.</exception>
    public static void RequireCapability(IPlatformClientCapabilitiesService clientCapabilities)
    {
        if (!clientCapabilities.Expects(capabilities => capabilities.ProgramDebugging))
        {
            throw new RpcErrorException(InvalidRequestCode, error: null!,
                $"The client did not advertise the '{nameof(ProgramDebugging)}' platform capability.");
        }
    }
}

/// <summary>
/// Handles <c>rdcore/session/debug/stack</c>: relays a client's request for the activations of the program that waits.
/// </summary>
internal sealed class SessionDebugStackHandler(IProgramDebugService debugging, IPlatformClientCapabilitiesService clientCapabilities)
    : RDCoreRequestHandler<SessionDebugStackParams, HostDebugStackResult>
{
    protected override Task<HostDebugStackResult> HandleAsync(SessionDebugStackParams request, CancellationToken token)
    {
        SessionDebugRelay.RequireCapability(clientCapabilities);
        return debugging.StackAsync(token);
    }
}

/// <summary>
/// Handles <c>rdcore/session/debug/variables</c>: relays a client's request for the variables of an activation of the program that waits.
/// </summary>
internal sealed class SessionDebugVariablesHandler(IProgramDebugService debugging, IPlatformClientCapabilitiesService clientCapabilities)
    : RDCoreRequestHandler<SessionDebugVariablesParams, HostDebugVariablesResult>
{
    protected override Task<HostDebugVariablesResult> HandleAsync(SessionDebugVariablesParams request, CancellationToken token)
    {
        SessionDebugRelay.RequireCapability(clientCapabilities);
        return debugging.VariablesAsync(request.FrameId, request.Scope, request.Reference, token);
    }
}

/// <summary>
/// Handles <c>rdcore/session/debug/evaluate</c>: has the component that owns the runtime session evaluate the expression a client wrote in an activation of the program
/// that waits.
/// </summary>
internal sealed class SessionDebugEvaluateHandler(IProgramDebugService debugging, IPlatformClientCapabilitiesService clientCapabilities)
    : RDCoreRequestHandler<SessionDebugEvaluateParams, HostDebugEvaluateResult>
{
    protected override Task<HostDebugEvaluateResult> HandleAsync(SessionDebugEvaluateParams request, CancellationToken token)
    {
        SessionDebugRelay.RequireCapability(clientCapabilities);
        return debugging.EvaluateAsync(request.FrameId, request.Expression, token);
    }
}
