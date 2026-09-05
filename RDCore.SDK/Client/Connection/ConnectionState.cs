namespace RDCore.SDK.Client.Connection;

/// <summary>
/// The lifecycle state of a <see cref="ChildConnection"/> to a supervised child process.
/// </summary>
public enum ConnectionStateValue
{
    /// <summary>Nothing has been started yet.</summary>
    NotStarted,
    /// <summary>The child process is being started.</summary>
    Spawning,
    /// <summary>The process is alive; the transport is connecting.</summary>
    Connecting,
    /// <summary>The transport is connected; the LSP initialization handshake is running.</summary>
    Initializing,
    /// <summary>Initialized; the connection is usable for requests and notifications.</summary>
    Ready,
    /// <summary>A graceful shutdown handshake is in progress.</summary>
    ShuttingDown,
    /// <summary>The child process has exited. Terminal.</summary>
    Exited,
    /// <summary>The connection failed unexpectedly (crash, connect timeout, handshake failure).</summary>
    Faulted,
}

/// <summary>
/// The base <see cref="ConnectionState"/> abstraction. One record per state, mirroring
/// <c>RDCore.SDK.Server.Services.States.ServerState</c>.
/// </summary>
public abstract record class ConnectionState(ConnectionStateValue Value)
{
    public static ConnectionState NotStarted { get; } = new NotStartedConnectionState();
    public static ConnectionState Spawning { get; } = new SpawningConnectionState();
    public static ConnectionState Connecting { get; } = new ConnectingConnectionState();
    public static ConnectionState Initializing { get; } = new InitializingConnectionState();
    public static ConnectionState Ready { get; } = new ReadyConnectionState();
    public static ConnectionState ShuttingDown { get; } = new ShuttingDownConnectionState();
    public static ConnectionState Exited { get; } = new ExitedConnectionState();
    public static ConnectionState Faulted(string reason) => new FaultedConnectionState(reason);

    /// <summary>Whether the connection has reached a terminal state and will not recover.</summary>
    public bool IsTerminal => Value is ConnectionStateValue.Exited;

    /// <summary>Whether a request or notification may be sent over the connection.</summary>
    public bool IsUsable => Value is ConnectionStateValue.Ready;

    /// <summary>
    /// Returns <paramref name="target"/> if the transition from this state is legal, otherwise throws.
    /// </summary>
    /// <exception cref="InvalidConnectionStateException"></exception>
    public ConnectionState AdvanceTo(ConnectionState target)
        => CanAdvanceTo(target.Value)
            ? target
            : throw new InvalidConnectionStateException(Value, target.Value);

    /// <summary>Whether a transition from this state to <paramref name="target"/> is legal.</summary>
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

public sealed record class NotStartedConnectionState() : ConnectionState(ConnectionStateValue.NotStarted);
public sealed record class SpawningConnectionState() : ConnectionState(ConnectionStateValue.Spawning);
public sealed record class ConnectingConnectionState() : ConnectionState(ConnectionStateValue.Connecting);
public sealed record class InitializingConnectionState() : ConnectionState(ConnectionStateValue.Initializing);
public sealed record class ReadyConnectionState() : ConnectionState(ConnectionStateValue.Ready);
public sealed record class ShuttingDownConnectionState() : ConnectionState(ConnectionStateValue.ShuttingDown);
public sealed record class ExitedConnectionState() : ConnectionState(ConnectionStateValue.Exited);

/// <param name="Reason">A short description of what went wrong.</param>
public sealed record class FaultedConnectionState(string Reason) : ConnectionState(ConnectionStateValue.Faulted);

/// <summary>
/// Thrown when a <see cref="ChildConnection"/> is asked to make an illegal state transition.
/// </summary>
public sealed class InvalidConnectionStateException(ConnectionStateValue from, ConnectionStateValue to)
    : Exception($"Illegal connection state transition: {from} -> {to}.")
{
    public ConnectionStateValue From { get; } = from;
    public ConnectionStateValue To { get; } = to;
}
