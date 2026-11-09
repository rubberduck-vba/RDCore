using RDCore.Runtime.Support.Host;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.CLI.Host.Handlers;

/// <summary>
/// Handles <c>rdcore/host/debug/pause</c>: stops the program that is running, at the next instruction, so that it waits there.
/// </summary>
internal sealed class HostDebugPauseHandler(IEnvironmentSessionProvider sessionProvider) : RDCoreRequestHandler<HostDebugPauseParams, DebugAck>
{
    protected override Task<DebugAck> HandleAsync(HostDebugPauseParams request, CancellationToken token)
        => Task.FromResult(new DebugAck { Acted = sessionProvider.IsComposed && sessionProvider.Execution.Pause() });
}
