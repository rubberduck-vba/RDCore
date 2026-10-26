namespace RDCore.External.Automation;

/// <summary>
/// A value that stands for an object of the session - an object of a class module of the program's, or of the standard library's (<c>Collection</c>) - wherever
/// it is given to an automation server: what tells it from the server's own objects, and which object of the session it is.
/// </summary>
/// <remarks>
/// An object of the program's is handed to a server as it is in MS-VBA - <c>d.Add "key", New Widget</c> - and is the same object when the server gives it back
/// (<c>d("key") Is w</c>): the server holds a stand-in for it, which answers the server's calls by calling the object in the session, and which is the object
/// again when it comes back.
/// </remarks>
public interface ISessionObjectStandIn
{
    /// <summary>The identity of the object, as the session writes it.</summary>
    string SessionObject { get; }
}

/// <summary>
/// An object of the session, as the session hands it to a server: the server's side makes the stand-in that its objects hold (<see cref="ISessionObjectStandIn"/>).
/// </summary>
/// <param name="SessionObject">The identity of the object, as the session writes it.</param>
/// <param name="Host">What answers the calls the server makes to it, and is told when the server lets go of it.</param>
public sealed record class SessionObjectReference(string SessionObject, ISessionObjectHost Host) : ISessionObjectStandIn;

/// <summary>
/// An object of the session, by its identity alone: what a stand-in is when it comes back across the external host, which the session finds its object by.
/// </summary>
/// <param name="SessionObject">The identity of the object, as the session writes it.</param>
public sealed record class SessionObjectIdentity(string SessionObject) : ISessionObjectStandIn;

/// <summary>
/// What answers for the objects of a session that a server holds: the calls the server makes to them, and its letting go of them.
/// </summary>
/// <remarks>
/// A stand-in counts the times its object was handed over (crossings), and says how many when it is let go of: the session holds the object for as long as a
/// crossing is not accounted for, as a server's reference holds a COM object.
/// </remarks>
public interface ISessionObjectHost
{
    /// <summary>
    /// Calls a member of an object of the session, as the server asked: what the server's <c>IDispatch::Invoke</c> of the stand-in is.
    /// </summary>
    /// <param name="call">The call.</param>
    /// <returns>What the member returned, as a server's value; the arguments passed by reference hold what the member left in them.</returns>
    /// <exception cref="AutomationException">The member fails, or there is no such member: what the server's call fails with.</exception>
    object? Invoke(SessionObjectCall call);

    /// <summary>
    /// A stand-in is let go of: the crossings it was made or found for are accounted for. Called on whatever thread lets go of it.
    /// </summary>
    /// <param name="sessionObject">The identity of the object.</param>
    /// <param name="crossings">How many times the object was handed over to the stand-in.</param>
    void Release(string sessionObject, int crossings);
}

/// <summary>
/// A call a server makes to an object of the session through its stand-in.
/// </summary>
/// <param name="SessionObject">The identity of the object.</param>
/// <param name="Name">The name of the member, when the server asked for it by its name; <see langword="null"/> for one it calls by its dispatch identifier.</param>
/// <param name="DispatchId">
/// The dispatch identifier the server called: the member's <c>VB_UserMemId</c>, <c>0</c> for its default member, <c>-4</c> for its enumerator - or the one the
/// stand-in gave the <see cref="Name"/>.
/// </param>
/// <param name="Invocation">How the member is called.</param>
/// <param name="Arguments">The arguments, in the order of the member's parameters; one passed by reference is written back.</param>
/// <param name="IsSynchronous">Whether a call the program waits for is what made it: <see cref="AutomationEvent.IsSynchronous"/>.</param>
/// <param name="Serve">What the server's thread does while an asynchronous call waits: <see cref="AutomationEvent.Serve"/>.</param>
public sealed record class SessionObjectCall(
    string SessionObject, string? Name, int DispatchId, AutomationInvocation Invocation, object?[] Arguments, bool IsSynchronous, Action<TimeSpan> Serve);
