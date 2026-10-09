using RDCore.SDK.Platform.Protocol;

namespace RDCore.CLI.Host.Handlers;

/// <summary>
/// Handles <c>rdcore/host/debug/errorBreak</c>: says which run-time errors a program under a debugger waits at.
/// </summary>
internal sealed class HostDebugErrorBreakHandler(IEnvironmentSessionProvider sessionProvider)
    : RDCoreRequestHandler<HostDebugErrorBreakParams, HostDebugAck>
{
    protected override Task<HostDebugAck> HandleAsync(HostDebugErrorBreakParams request, CancellationToken token)
    {
        if (!sessionProvider.IsComposed)
        {
            return Task.FromResult(new HostDebugAck());
        }

        sessionProvider.Execution.SetErrorBreak(request.Mode);
        return Task.FromResult(new HostDebugAck { Acted = true });
    }
}
