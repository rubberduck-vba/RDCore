using RDCore.External.Automation;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Runtime.Execution.External.Automation;

/// <summary>
/// Listens to the events of a server's object for as long as a <c>WithEvents</c> variable holds it (<strong>MS-VBAL §5.4.3.9</strong>).
/// </summary>
/// <remarks>
/// An object of the workspace raises its events from its own code, and needs nothing to be listened to. An object of a server raises them when the server pleases, so the
/// session asks to be told when the first variable holds it, and stops asking when the last does not.
/// </remarks>
internal static class ExternalEvents
{
    /// <summary>
    /// Starts listening to the events of <paramref name="source"/>, if it is a server's object that is not listened to already.
    /// </summary>
    /// <param name="session">The session whose variables hold the object.</param>
    /// <param name="source">The object.</param>
    public static void Listen(IRuntimeSession session, VBRuntimeObjectId source)
    {
        if (!session.ExternalObjects.TryGet(source, out var owner, out var handle) || owner is not IAutomationServer server
            || !session.ExternalObjects.TryBeginListening(source))
        {
            return;
        }

        try
        {
            using (session.Turn.Yield())
            {
                server.Advise(handle, new ServerEventRouter(session, server, source));
            }
        }
        catch (AutomationException)
        {
            // an object that cannot say what it raises raises nothing the program can handle, and is no worse for having been asked.
            _ = session.ExternalObjects.TryEndListening(source);
        }
    }

    /// <summary>
    /// Stops listening to the events of <paramref name="source"/> when no variable holds it any more.
    /// </summary>
    /// <param name="session">The session whose variables held the object.</param>
    /// <param name="source">The object.</param>
    public static void StopWhenUnheard(IRuntimeSession session, VBRuntimeObjectId source)
    {
        if (session.Objects.EventSubscribers(source).Count != 0
            || !session.ExternalObjects.TryGet(source, out var owner, out var handle) || owner is not IAutomationServer server
            || !session.ExternalObjects.TryEndListening(source))
        {
            return;
        }

        using (session.Turn.Yield())
        {
            server.Unadvise(handle);
        }
    }
}
