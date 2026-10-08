using RDCore.SDK.Platform.Protocol;

namespace RDCore.CLI.Host.Handlers;

/// <summary>
/// Handles <c>rdcore/host/debug/pause</c>: stops the program that is running, at the next instruction, so that it waits there.
/// </summary>
internal sealed class HostDebugPauseHandler(IEnvironmentSessionProvider sessionProvider) : RDCoreRequestHandler<HostDebugPauseParams, HostDebugAck>
{
    protected override Task<HostDebugAck> HandleAsync(HostDebugPauseParams request, CancellationToken token)
        => Task.FromResult(new HostDebugAck { Acted = sessionProvider.IsComposed && sessionProvider.Execution.Pause() });
}
