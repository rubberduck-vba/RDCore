using RDCore.SDK.Platform.Protocol;

namespace RDCore.CLI.Host.Handlers;

/// <summary>
/// Handles <c>rdcore/host/debug/terminate</c>: ends the program that is running or waits.
/// </summary>
internal sealed class HostDebugTerminateHandler(IEnvironmentSessionProvider sessionProvider) : RDCoreRequestHandler<HostDebugTerminateParams, DebugAck>
{
    protected override async Task<DebugAck> HandleAsync(HostDebugTerminateParams request, CancellationToken token)
        => new() { Acted = sessionProvider.IsComposed && await sessionProvider.Execution.TerminateAsync(request.Wipe) };
}
