namespace RDCore.SDK.Server;

/// <summary>
/// Marks a request that an RDCore server handles as it arrives, beside the requests before it that are still being handled, rather than after them.
/// </summary>
/// <remarks>
/// <para>
/// OmniSharp handles a request once those before it are done: a request it runs in order waits for every request before it, and one it runs in parallel waits
/// for those before it that run in order. A request that runs a program - <c>rdcore/host/execute</c>, <c>rdcore/host/debug/resume</c> - is not done until the
/// program waits or is over, and a request that would stop it would wait for it to stop: a program that never ends could not be paused or ended.
/// </para>
/// <para>
/// A request marked with this acts on what another request is doing and is safe to handle while it does: pausing or ending the program that runs, saying which
/// errors it waits at. Its handler must not wait for anything that waits for the request that runs - an unmarked request to another server, the program
/// itself - and may wait only for requests that are marked too: an end the program cannot come to at once is answered after a set while, as asked and still
/// stopping. The marked requests are handled in the order they arrive in, one after the other, so one that waited without bound would hold every one after it;
/// the order of every other request and notification is not changed. A <c>$/cancelRequest</c> does not reach it.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class HandledOnArrivalAttribute : Attribute
{
}
