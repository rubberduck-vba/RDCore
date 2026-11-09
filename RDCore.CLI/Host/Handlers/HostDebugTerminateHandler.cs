using RDCore.Runtime.Support.Host;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Platform.Components;

namespace RDCore.CLI.Host.Handlers;

/// <summary>
/// Handles <c>rdcore/host/debug/terminate</c>: ends the program that is running or waits.
/// </summary>
/// <remarks>
/// A program that runs is ended at its next instruction, which a program in a call outside it - a <c>Declare</c>, an object, a dialog - comes to when the call
/// returns: the request is answered once the program is ended, or after <see cref="StopWait"/> with the end asked of it, which stands, and says the program is
/// still stopping (<see cref="DebugAck.Stopping"/>). The request is handled as it arrives (<see cref="SDK.Server.HandledOnArrivalAttribute"/>), and nothing
/// else bounds it.
/// </remarks>
internal sealed class HostDebugTerminateHandler(IEnvironmentSessionProvider sessionProvider) : RDCoreRequestHandler<HostDebugTerminateParams, DebugAck>
{
    /// <summary>How long the request waits for a program that runs to stop.</summary>
    internal static readonly TimeSpan StopWait = TimeSpan.FromSeconds(5);

    protected override async Task<DebugAck> HandleAsync(HostDebugTerminateParams request, CancellationToken token)
    {
        if (!sessionProvider.IsComposed)
        {
            return new DebugAck();
        }

        using var bound = CancellationTokenSource.CreateLinkedTokenSource(token);
        bound.CancelAfter(StopWait);
        return await sessionProvider.Execution.TerminateAsync(request.Wipe, bound.Token);
    }
}
