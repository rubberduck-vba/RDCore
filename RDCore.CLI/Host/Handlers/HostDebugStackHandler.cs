using RDCore.Runtime.Support.Host;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.CLI.Host.Handlers;

/// <summary>
/// Handles <c>rdcore/host/debug/stack</c>: the activations of the program that waits, innermost first.
/// </summary>
internal sealed class HostDebugStackHandler(IEnvironmentSessionProvider sessionProvider) : RDCoreRequestHandler<HostDebugStackParams, DebugStackResult>
{
    protected override Task<DebugStackResult> HandleAsync(HostDebugStackParams request, CancellationToken token)
        => Task.FromResult(sessionProvider.IsComposed ? sessionProvider.Execution.Stack() : new DebugStackResult());
}
