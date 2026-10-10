namespace RDCore.External.Automation;

/// <summary>
/// How a member of an automation server is reached.
/// </summary>
/// <remarks>
/// The same four ways that <c>IDispatch::Invoke</c> distinguishes, which is also how a library's description kinds its members.
/// </remarks>
public enum AutomationInvocation
{
    /// <summary>A method, or a property that takes arguments, is read: <c>DISPATCH_METHOD</c> together with <c>DISPATCH_PROPERTYGET</c>, which is what VBA itself asks for.</summary>
    Get,

    /// <summary>A method is called; there is no value to read it as.</summary>
    Method,

    /// <summary>A property is assigned a value (<c>Property Let</c>): <c>DISPATCH_PROPERTYPUT</c>.</summary>
    Let,

    /// <summary>A property is assigned an object (<c>Property Set</c>): <c>DISPATCH_PROPERTYPUTREF</c>.</summary>
    Set,
}

/// <summary>
/// A failure of an automation server, as it reports one: a status code and what it has to say about it.
/// </summary>
/// <remarks>
/// The code is the server's own <c>HRESULT</c>. Which VBA error it is - an Excel <c>1004</c> arrives as <c>0x800A03EC</c> - is decided where the error is
/// raised, by the runtime, not by whatever reached the server.
/// </remarks>
public sealed class AutomationException : Exception
{
    /// <summary>
    /// Creates the failure.
    /// </summary>
    /// <param name="hResult">The status code the server reported.</param>
    /// <param name="message">What the server said, if it said anything.</param>
    /// <param name="source">The name of the server that said it, if it named itself.</param>
    public AutomationException(int hResult, string? message = null, string? source = null)
        : base(message)
    {
        HResult = hResult;
        Source = source;
    }
}

/// <summary>
/// Everything that touches the automation servers of a machine: creating an object by its name, calling a member by its name, and letting go.
/// </summary>
/// <remarks>
/// The seam under the runtime's calls to the objects of a library, and the only part of them that is not the same on every platform. Everything above it speaks in
/// neutral values: <see langword="null"/> is an <c>Empty</c> and <see cref="DBNull"/> a <c>Null</c>, <see cref="System.Reflection.Missing"/> an omitted
/// argument, and a server's object is a handle nothing here looks inside. A <c>Currency</c> is a <see cref="System.Runtime.InteropServices.CurrencyWrapper"/>
/// and an <c>Error</c> an <see cref="System.Runtime.InteropServices.ErrorWrapper"/>, so that a server told apart the types the language tells apart.
/// <para>
/// The server is also the owner of the objects it made (<see cref="SDK.Runtime.Abstract.Execution.IExternalObjectOwner.Release"/> lets one go): a session outlives
/// the pipelines that run its programs, so what holds its objects has to be there for all of them, and there is one server to a machine.
/// </para>
/// <para>
/// ⚠️ An implementation is called from whichever thread the pipeline runs on and is responsible for running the server on the thread the server needs.
/// </para>
/// </remarks>
public interface IAutomationServer : SDK.Runtime.Abstract.Execution.IExternalObjectOwner
{
    /// <summary>
    /// Whether this machine has automation servers to reach at all.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Creates an object by the name its class is registered under (<c>Scripting.Dictionary</c>).
    /// </summary>
    /// <param name="progId">The programmatic identifier of the class.</param>
    /// <returns>The handle of the new object.</returns>
    /// <exception cref="AutomationException">The class is not registered here, or the server would not start.</exception>
    object CreateObject(string progId);

    /// <summary>
    /// Calls a member of an object by its name.
    /// </summary>
    /// <param name="target">The handle of the object.</param>
    /// <param name="member">The name of the member.</param>
    /// <param name="invocation">How the member is reached.</param>
    /// <param name="arguments">
    /// The arguments in the member's order, a value being assigned last. An argument that is passed by reference and that the server wrote to holds what it
    /// wrote when this returns.
    /// </param>
    /// <param name="byReference">Whether each argument is passed by reference; the same length as <paramref name="arguments"/>.</param>
    /// <returns>What the member returned: <see langword="null"/> for a member that returns nothing, or an <c>Empty</c>.</returns>
    /// <exception cref="AutomationException">The member does not exist, would not accept the arguments, or failed.</exception>
    /// <param name="culture">
    /// The locale the call is made in: how a server reads a number or a date it is given as text, and the language it answers in. The environment's own;
    /// the invariant culture is a locale no server has, and is made the one that every server has.
    /// </param>
    object? Invoke(object target, string member, AutomationInvocation invocation, object?[] arguments, bool[] byReference, System.Globalization.CultureInfo culture);

    /// <summary>
    /// Gets the name a server gives the class of an object, qualified by the library that declares it (<c>Excel._Worksheet</c>), if it gives one.
    /// </summary>
    /// <remarks>
    /// What an object that a member declares to be an <c>Object</c> is an instance of; <see langword="null"/> when the server describes none.
    /// </remarks>
    /// <param name="target">The handle of the object.</param>
    string? ClassNameOf(object target);

    /// <summary>
    /// Moves the enumerator an object's enumeration member returned (<c>_NewEnum</c>) to its next member.
    /// </summary>
    /// <remarks>
    /// MS-VBAL §5.4.2.4 leaves the enumeration of an object implementation-defined; for a server's it is an <c>IEnumVARIANT</c>, which a <c>For Each</c> asks
    /// for each member in turn.
    /// </remarks>
    /// <param name="enumerator">The handle of the enumerator.</param>
    /// <param name="current">The member it moved to.</param>
    /// <returns><see langword="false"/> when the members have run out.</returns>
    /// <exception cref="AutomationException">The enumerator failed.</exception>
    bool MoveNext(object enumerator, out object? current);

    /// <summary>
    /// Returns an enumerator to before the first member.
    /// </summary>
    /// <param name="enumerator">The handle of the enumerator.</param>
    /// <exception cref="AutomationException">The enumerator cannot be reset.</exception>
    void Reset(object enumerator);

    /// <summary>
    /// Starts listening to the events of an object.
    /// </summary>
    /// <remarks>
    /// An object that raises none, or that does not say which, is listened to for nothing and is not an error. Every event it raises is handed to
    /// <paramref name="sink"/> by <see cref="AutomationEvents.Deliver"/>, which is what keeps an event that no call is waiting for from being handled in the middle of a procedure.
    /// </remarks>
    /// <param name="source">The handle of the object that raises the events.</param>
    /// <param name="sink">Where they go.</param>
    void Advise(object source, IAutomationEventSink sink);

    /// <summary>
    /// Stops listening to the events of an object. An object that is not listened to is left as it is.
    /// </summary>
    /// <param name="source">The handle of the object that raises the events.</param>
    void Unadvise(object source);
}

/// <summary>
/// Where the events of a server's object go.
/// </summary>
/// <remarks>
/// The session's side of an event: it is told which event, and with what, on whatever thread the server raised it on.
/// </remarks>
public interface IAutomationEventSink
{
    /// <summary>
    /// Whether an event that no call is waiting for may be handled now - <see cref="ISessionTurn.IsOpenForEvents"/>.
    /// </summary>
    bool IsOpenForEvents { get; }

    /// <summary>
    /// Says that an event that no call is waiting for waits to be handled, until the returned scope is disposed.
    /// </summary>
    IDisposable Waiting();

    /// <summary>
    /// Handles an event: the procedures that handle it run, and return.
    /// </summary>
    /// <param name="name">The name of the event.</param>
    /// <param name="arguments">
    /// What the server raised it with, in the order of the event's parameters. An argument the server passed by reference holds, when this returns, what the procedures left
    /// in it - the <c>Cancel</c> of an event that can be cancelled.
    /// </param>
    void OnEvent(string name, object?[] arguments);
}

/// <summary>
/// How a server hands an event over.
/// </summary>
public static class AutomationEvents
{
    /// <summary>
    /// Hands an event to its sink, when it may be handled.
    /// </summary>
    /// <remarks>
    /// A <em>synchronous</em> event is the answer to a call that was made and is waited for: it is handled at once, inside the call. Any other event is <em>asynchronous</em>,
    /// and is handled between two activations: it waits until nothing runs, or until the program pumps (<c>DoEvents</c>). It waits on the thread of the server, which has
    /// the program's own calls to make in the meantime - a program that is running makes them, and blocking the thread they are made on would keep it from ever pumping -
    /// so <paramref name="serve"/> is what the thread does with the time it waits.
    /// </remarks>
    /// <param name="sink">Where the event goes.</param>
    /// <param name="name">The name of the event.</param>
    /// <param name="arguments">What the server raised it with.</param>
    /// <param name="synchronous">Whether a call that the program waits for is what raised it.</param>
    /// <param name="serve">Waits up to the given time, doing the work that is asked of the thread meanwhile.</param>
    public static void Deliver(IAutomationEventSink sink, string name, object?[] arguments, bool synchronous, Action<TimeSpan> serve)
    {
        if (synchronous)
        {
            sink.OnEvent(name, arguments);
            return;
        }

        using (sink.Waiting())
        {
            while (!sink.IsOpenForEvents)
            {
                serve(TimeSpan.FromMilliseconds(10));
            }

            sink.OnEvent(name, arguments);
        }
    }
}
