using RDCore.SDK.Platform.Protocol;

namespace RDCore.CLI.Host.Handlers;

/// <summary>
/// Handles <c>rdcore/host/debug/variables</c>: the variables of an activation of the program that waits, or the parts of one of them.
/// </summary>
internal sealed class HostDebugVariablesHandler(IEnvironmentSessionProvider sessionProvider)
    : RDCoreRequestHandler<HostDebugVariablesParams, HostDebugVariablesResult>
{
    protected override Task<HostDebugVariablesResult> HandleAsync(HostDebugVariablesParams request, CancellationToken token)
        => Task.FromResult(sessionProvider.IsComposed
            ? sessionProvider.Execution.Variables(request.FrameId, request.Scope, request.Reference)
            : new HostDebugVariablesResult());
}
