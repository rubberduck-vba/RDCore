using OmniSharp.Extensions.JsonRpc;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.CLI.App.Repl;

/// <summary>
/// Where the stops of the handlers of events that run while no program runs reach the shell: a handler of a form's event that stops at a <c>Stop</c> or a
/// breakpoint breaks there, as VB6 breaks in it, and the shell is in break mode at its prompt.
/// </summary>
/// <remarks>
/// The handler of the notification is made by the language client's container, and the shell is not: this is what both have, in the outer container.
/// </remarks>
internal sealed class ReplSessionStops
{
    /// <summary>
    /// Raised for each stop, on the thread the notification is handled on.
    /// </summary>
    public event Action<ExecuteResult>? Stopped;

    /// <summary>
    /// Tells the shell of a stop.
    /// </summary>
    /// <param name="stop">Where the handler waits.</param>
    public void Publish(ExecuteResult stop) => Stopped?.Invoke(stop);
}

/// <summary>
/// Handles <c>rdcore/session/debug/stopped</c>: a handler of an event stopped while no program runs, and waits.
/// </summary>
/// <param name="stops">Where the shell takes it.</param>
internal sealed class ReplSessionStoppedHandler(ReplSessionStops stops) : IJsonRpcNotificationHandler<SessionDebugStoppedNotification>
{
    /// <inheritdoc/>
    public Task<MediatR.Unit> Handle(SessionDebugStoppedNotification request, CancellationToken cancellationToken)
    {
        stops.Publish(request.Stop);
        return MediatR.Unit.Task;
    }
}
