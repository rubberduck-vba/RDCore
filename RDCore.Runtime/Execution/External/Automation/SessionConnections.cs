using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using System.Runtime.CompilerServices;

namespace RDCore.Runtime.Execution.External.Automation;

/// <summary>
/// The objects of a session that a program connected to a server's object's events itself, through its connection point (<c>ConnectToConnectionPoint</c>):
/// a sink, which the object calls the members of by the dispatch identifiers it raises its events with, for as long as it is connected.
/// </summary>
/// <remarks>
/// A connection holds its sink, as a connection point holds the sink it is advised: an object a program connects and lets go of is still called, until it is
/// disconnected. The session outlives the pipelines that run its programs, so its connections are kept by the session.
/// </remarks>
internal sealed class SessionConnections
{
    private static readonly ConditionalWeakTable<IRuntimeSession, SessionConnections> Sessions = [];

    private readonly object _gate = new();
    private readonly List<Connection> _connections = [];
    private int _lastCookie;

    /// <summary>The connections of <paramref name="session"/>.</summary>
    public static SessionConnections Of(IRuntimeSession session) => Sessions.GetValue(session, _ => new SessionConnections());

    /// <summary>
    /// Connects <paramref name="sink"/> to the events of <paramref name="source"/>.
    /// </summary>
    /// <param name="source">The server's object.</param>
    /// <param name="sink">The object of the session the events call.</param>
    /// <param name="events">The interface the events are raised through, as the program asked for it.</param>
    /// <param name="holder">The reference the connection holds the sink by.</param>
    /// <returns>The connection's cookie, which disconnects it: never 0.</returns>
    public int Connect(VBRuntimeObjectId source, VBRuntimeObjectId sink, Guid events, IBindingHandle holder)
    {
        lock (_gate)
        {
            var cookie = ++_lastCookie;
            _connections.Add(new Connection(cookie, source, sink, events, holder));
            return cookie;
        }
    }

    /// <summary>
    /// Disconnects the connection <paramref name="cookie"/> of <paramref name="source"/>.
    /// </summary>
    /// <returns>The connection, which held its sink; <see langword="null"/> for a cookie that is no connection of the object.</returns>
    public Connection? Disconnect(VBRuntimeObjectId source, int cookie)
    {
        lock (_gate)
        {
            var index = _connections.FindIndex(connection => connection.Cookie == cookie && connection.Source.Equals(source));
            if (index < 0)
            {
                return null;
            }

            var connection = _connections[index];
            _connections.RemoveAt(index);
            return connection;
        }
    }

    /// <summary>
    /// The connections of <paramref name="source"/>, in the order they were made.
    /// </summary>
    /// <param name="source">The server's object.</param>
    public IReadOnlyList<Connection> Of(VBRuntimeObjectId source)
    {
        lock (_gate)
        {
            return [.. _connections.Where(connection => connection.Source.Equals(source))];
        }
    }

    /// <summary>
    /// Whether anything is connected to <paramref name="source"/>.
    /// </summary>
    public bool IsConnected(VBRuntimeObjectId source)
    {
        lock (_gate)
        {
            return _connections.Any(connection => connection.Source.Equals(source));
        }
    }

    /// <summary>
    /// A sink connected to a server's object.
    /// </summary>
    /// <param name="Cookie">What disconnects it.</param>
    /// <param name="Source">The server's object.</param>
    /// <param name="Sink">The object of the session the events call.</param>
    /// <param name="Events">The interface the events are raised through.</param>
    /// <param name="Holder">The reference the connection holds the sink by.</param>
    public sealed record class Connection(int Cookie, VBRuntimeObjectId Source, VBRuntimeObjectId Sink, Guid Events, IBindingHandle Holder);
}
