using RDCore.Runtime.Support.Host;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Platform.Components;

namespace RDCore.CLI.Host.Handlers;

/// <summary>
/// Handles <c>rdcore/host/debug/errorBreak</c>: says which run-time errors a program under a debugger waits at.
/// </summary>
internal sealed class HostDebugErrorBreakHandler(IEnvironmentSessionProvider sessionProvider)
    : RDCoreRequestHandler<HostDebugErrorBreakParams, DebugAck>
{
    protected override Task<DebugAck> HandleAsync(HostDebugErrorBreakParams request, CancellationToken token)
    {
        if (!sessionProvider.IsComposed)
        {
            return Task.FromResult(new DebugAck());
        }

        sessionProvider.Execution.SetErrorBreak(request.Mode);
        return Task.FromResult(new DebugAck { Acted = true });
    }
}
