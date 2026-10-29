using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.JsonRpc.Server;
using RDCore.LanguageServer.Debugging;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.SDK.Client;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server.Services;

namespace RDCore.LanguageServer.Runtime;

/// <summary>
/// Handles <c>rdcore/session/debug/resume</c>: relays a client's request to go on with the program it ran under a debugger to the component that owns the runtime
/// session, and answers what that answers - where the program waits next, or how it ended.
/// </summary>
/// <remarks>
/// The program is checked against the edits made while it waited, which the host has once the workspace is loaded as the documents are now: the request is
/// relayed then (<see cref="IDocumentLifecycleService.WhenAllLoadedAsync"/>), lest a module the edit changed be checked against what the others were. Once it is
/// answered - the program went on, is over, or was refused - what it does not have of the edits is asked again.
/// </remarks>
internal sealed class SessionDebugResumeHandler(
    IProgramDebugService debugging, IPlatformClientCapabilitiesService clientCapabilities, IEditStatusMonitor editStatus, IDocumentLifecycleService lifecycle)
    : RDCoreRequestHandler<SessionDebugResumeParams, ExecuteSessionResult>
{
    protected override async Task<ExecuteSessionResult> HandleAsync(SessionDebugResumeParams request, CancellationToken token)
    {
        SessionDebugRelay.RequireCapability(clientCapabilities);
        await lifecycle.WhenAllLoadedAsync(token);
        var result = await debugging.ResumeAsync(request.Step, token);
        await editStatus.CheckAsync(token);
        return result;
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
/// <remarks>
/// A point moved to applies the edits made while the program waited, as going on does: the request is relayed once the workspace is loaded as the documents are
/// now, and what the program does not have of the edits is asked again once it is answered.
/// </remarks>
internal sealed class SessionDebugGotoHandler(
    IProgramDebugService debugging, IPlatformClientCapabilitiesService clientCapabilities, IEditStatusMonitor editStatus, IDocumentLifecycleService lifecycle)
    : RDCoreRequestHandler<SessionDebugGotoParams, HostDebugGotoResult>
{
    protected override async Task<HostDebugGotoResult> HandleAsync(SessionDebugGotoParams request, CancellationToken token)
    {
        SessionDebugRelay.RequireCapability(clientCapabilities);
        await lifecycle.WhenAllLoadedAsync(token);
        var result = await debugging.GotoAsync(request.Line, request.Label, token);
        await editStatus.CheckAsync(token);
        return result;
    }
}

/// <summary>
/// Handles <c>rdcore/session/debug/editStatus</c>: relays a client's question of which edits made while the program waits are not applied to it.
/// </summary>
/// <remarks>
/// The edits are those of the workspace loaded as the documents are now, which the question waits for; what the host answers is what the diagnostics of the
/// edited modules are of, too (<see cref="IEditStatusMonitor"/>).
/// </remarks>
internal sealed class SessionDebugEditStatusHandler(IPlatformClientCapabilitiesService clientCapabilities, IEditStatusMonitor editStatus, IDocumentLifecycleService lifecycle)
    : RDCoreRequestHandler<SessionDebugEditStatusParams, HostDebugEditStatusResult>
{
    protected override async Task<HostDebugEditStatusResult> HandleAsync(SessionDebugEditStatusParams request, CancellationToken token)
    {
        SessionDebugRelay.RequireCapability(clientCapabilities);
        await lifecycle.WhenAllLoadedAsync(token);
        await editStatus.CheckAsync(token);
        return editStatus.Current;
    }
}

/// <summary>
/// Handles <c>rdcore/session/debug/terminate</c>: relays a client's request to end the program that is running or waits.
/// </summary>
/// <remarks>A program that is over has no edits it does not have: what is said of them is cleared once it is answered.</remarks>
internal sealed class SessionDebugTerminateHandler(IProgramDebugService debugging, IPlatformClientCapabilitiesService clientCapabilities, IEditStatusMonitor editStatus)
    : RDCoreRequestHandler<SessionDebugTerminateParams, HostDebugAck>
{
    protected override async Task<HostDebugAck> HandleAsync(SessionDebugTerminateParams request, CancellationToken token)
    {
        SessionDebugRelay.RequireCapability(clientCapabilities);
        var result = await debugging.TerminateAsync(request.Wipe, token);
        await editStatus.CheckAsync(token);
        return result;
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
        return debugging.SetBreakpointsAsync(request.ModuleName, request.Breakpoints, token);
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

/// <summary>
/// Handles <c>rdcore/session/debug/execute</c>: has the component that owns the runtime session run the statement a client wrote in an activation of the program that
/// waits.
/// </summary>
/// <remarks>A statement typed at the stop may end the program (<c>End</c>): what it does not have of the edits is asked again once it is answered.</remarks>
internal sealed class SessionDebugExecuteHandler(IProgramDebugService debugging, IPlatformClientCapabilitiesService clientCapabilities, IEditStatusMonitor editStatus)
    : RDCoreRequestHandler<SessionDebugExecuteParams, HostDebugEvaluateResult>
{
    protected override async Task<HostDebugEvaluateResult> HandleAsync(SessionDebugExecuteParams request, CancellationToken token)
    {
        SessionDebugRelay.RequireCapability(clientCapabilities);
        var result = await debugging.ExecuteAsync(request.FrameId, request.Statement, token);

        // the host has answered: a request cancelled now is not one whose answer is lost.
        await editStatus.CheckAsync(CancellationToken.None);
        return result;
    }
}
