namespace RDCore.SDK.Client.Connection;

/// <summary>
/// The lifecycle state of a <see cref="ChildConnection"/> to a supervised child process.
/// </summary>
public enum ConnectionStateValue
{
    /// <summary>
    /// Nothing has been started yet.
    /// </summary>
    NotStarted,

    /// <summary>
    /// The child process is being started.
    /// </summary>
    Spawning,

    /// <summary>
    /// The process is alive; the transport is connecting.
    /// </summary>
    Connecting,

    /// <summary>
    /// The transport is connected; the LSP initialization handshake is running.
    /// </summary>
    Initializing,

    /// <summary>
    /// Initialized; the connection is usable for requests and notifications.
    /// </summary>
    Ready,

    /// <summary>
    /// A graceful shutdown handshake is in progress.
    /// </summary>
    ShuttingDown,

    /// <summary>
    /// The child process has exited. Terminal.
    /// </summary>
    Exited,

    /// <summary>
    /// The connection failed unexpectedly (crash, connect timeout, handshake failure).
    /// </summary>
    Faulted,
}

/// <summary>
/// The base <see cref="ConnectionState"/> abstraction. One record per state, mirroring
/// <c>RDCore.SDK.Server.Services.States.ServerState</c>.
/// </summary>
/// <param name="Value">The <see cref="ConnectionStateValue"/> this state represents.</param>
public abstract record class ConnectionState(ConnectionStateValue Value)
{
    /// <summary>
    /// The initial state: nothing started.
    /// </summary>
    public static ConnectionState NotStarted { get; } = new NotStartedConnectionState();

    /// <summary>
    /// The child process is being started.
    /// </summary>
    public static ConnectionState Spawning { get; } = new SpawningConnectionState();

    /// <summary>
    /// The process is alive; the transport is connecting.
    /// </summary>
    public static ConnectionState Connecting { get; } = new ConnectingConnectionState();

    /// <summary>
    /// The transport is connected; the LSP initialization handshake is running.
    /// </summary>
    public static ConnectionState Initializing { get; } = new InitializingConnectionState();

    /// <summary>
    /// The connection is initialized and usable.
    /// </summary>
    public static ConnectionState Ready { get; } = new ReadyConnectionState();

    /// <summary>
    /// A graceful shutdown handshake is in progress.
    /// </summary>
    public static ConnectionState ShuttingDown { get; } = new ShuttingDownConnectionState();

    /// <summary>
    /// The child process has exited. Terminal.
    /// </summary>
    public static ConnectionState Exited { get; } = new ExitedConnectionState();

    /// <summary>
    /// The connection has failed unexpectedly.
    /// </summary>
    /// <param name="reason">A short description of what went wrong.</param>
    /// <returns>A <see cref="FaultedConnectionState"/> carrying <paramref name="reason"/>.</returns>
    public static ConnectionState Faulted(string reason) => new FaultedConnectionState(reason);

    /// <summary>
    /// Whether the connection has reached a terminal state and will not recover.
    /// </summary>
    public bool IsTerminal => Value is ConnectionStateValue.Exited;

    /// <summary>
    /// Whether a request or notification may be sent over the connection.
    /// </summary>
    public bool IsUsable => Value is ConnectionStateValue.Ready;

    /// <summary>
    /// Returns <paramref name="target"/> if the transition from this state is legal, otherwise throws.
    /// </summary>
    /// <param name="target">The state to transition to.</param>
    /// <returns><paramref name="target"/>, when the transition is legal.</returns>
    /// <exception cref="InvalidConnectionStateException">The transition from this state to <paramref name="target"/> is not legal.</exception>
    public ConnectionState AdvanceTo(ConnectionState target)
        => CanAdvanceTo(target.Value)
            ? target
            : throw new InvalidConnectionStateException(Value, target.Value);

    /// <summary>
    /// Whether a transition from this state to <paramref name="target"/> is legal.
    /// </summary>
    /// <param name="target">The candidate next state.</param>
    public bool CanAdvanceTo(ConnectionStateValue target) => (Value, target) switch
    {
        (ConnectionStateValue.NotStarted, ConnectionStateValue.Spawning) => true,
        (ConnectionStateValue.Spawning, ConnectionStateValue.Connecting) => true,
        (ConnectionStateValue.Connecting, ConnectionStateValue.Initializing) => true,
        (ConnectionStateValue.Initializing, ConnectionStateValue.Ready) => true,
        (ConnectionStateValue.Ready, ConnectionStateValue.ShuttingDown) => true,
        (ConnectionStateValue.ShuttingDown, ConnectionStateValue.Exited) => true,
        // an unexpected failure can happen from any live state:
        (not ConnectionStateValue.Exited and not ConnectionStateValue.Faulted, ConnectionStateValue.Faulted) => true,
        // a faulted connection is either restarted or given up on:
        (ConnectionStateValue.Faulted, ConnectionStateValue.Spawning) => true,
        (ConnectionStateValue.Faulted, ConnectionStateValue.Exited) => true,
        _ => false,
    };
}

/// <summary>
/// The connection has not been started.
/// </summary>
public sealed record class NotStartedConnectionState() : ConnectionState(ConnectionStateValue.NotStarted);

/// <summary>
/// The child process is being started.
/// </summary>
public sealed record class SpawningConnectionState() : ConnectionState(ConnectionStateValue.Spawning);

/// <summary>
/// The process is alive and the transport is connecting.
/// </summary>
public sealed record class ConnectingConnectionState() : ConnectionState(ConnectionStateValue.Connecting);

/// <summary>
/// The transport is connected and the LSP initialization handshake is running.
/// </summary>
public sealed record class InitializingConnectionState() : ConnectionState(ConnectionStateValue.Initializing);

/// <summary>
/// The connection is initialized and usable for requests and notifications.
/// </summary>
public sealed record class ReadyConnectionState() : ConnectionState(ConnectionStateValue.Ready);

/// <summary>
/// A graceful shutdown handshake is in progress.
/// </summary>
public sealed record class ShuttingDownConnectionState() : ConnectionState(ConnectionStateValue.ShuttingDown);

/// <summary>
/// The child process has exited. This state is terminal.
/// </summary>
public sealed record class ExitedConnectionState() : ConnectionState(ConnectionStateValue.Exited);

/// <summary>
/// The connection failed unexpectedly (crash, connect timeout, or handshake failure).
/// </summary>
/// <param name="Reason">A short description of what went wrong.</param>
public sealed record class FaultedConnectionState(string Reason) : ConnectionState(ConnectionStateValue.Faulted);

/// <summary>
/// Thrown when a <see cref="ChildConnection"/> is asked to make an illegal state transition.
/// </summary>
/// <param name="from">The state the connection was in.</param>
/// <param name="to">The state the connection was asked to move to.</param>
public sealed class InvalidConnectionStateException(ConnectionStateValue from, ConnectionStateValue to)
    : Exception($"Illegal connection state transition: {from} -> {to}.")
{
    /// <summary>
    /// The state the connection was in.
    /// </summary>
    public ConnectionStateValue From { get; } = from;

    /// <summary>
    /// The state the connection was asked to move to.
    /// </summary>
    public ConnectionStateValue To { get; } = to;
}
