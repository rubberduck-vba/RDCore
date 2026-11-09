using RDCore.Runtime.Support.Host;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.CLI.Host.Handlers;

/// <summary>
/// Handles <c>rdcore/host/debug/editStatus</c>: which edits made while the program waits are not applied to it.
/// </summary>
internal sealed class HostDebugEditStatusHandler(IEnvironmentSessionProvider sessionProvider) : RDCoreRequestHandler<HostDebugEditStatusParams, DebugEditStatusResult>
{
    protected override Task<DebugEditStatusResult> HandleAsync(HostDebugEditStatusParams request, CancellationToken token)
        => Task.FromResult(sessionProvider.IsComposed ? sessionProvider.Execution.EditStatus() : new DebugEditStatusResult());
}
