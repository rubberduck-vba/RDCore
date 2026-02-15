namespace RDCore.Server.States;

/// <summary>
/// Defines methods for managing and transitioning the operational lifecycle state of a server instance.
/// </summary>
internal interface IServerStateProvider
{
    ServerState State { get; }
    /// <summary>
    /// Sets the server state to <c>Initializing</c> if the current state is <c>Starting</c>, otherwise throws an <see cref="InvalidServerStateException"/>.
    /// </summary>
    /// <exception cref="InvalidServerStateException"></exception>
    void OnInitialize();
    /// <summary>
    /// Sets the server state to <c>Running</c> if the current state is <c>Initializing</c>, otherwise throws an <see cref="InvalidServerStateException"/>.
    /// </summary>
    /// <exception cref="InvalidServerStateException"></exception>
    void OnInitialized();
    /// <summary>
    /// Sets the server state to <c>ShuttingDown</c> if the current state is <c>Starting</c> or <c>Running</c>, otherwise throws an <see cref="InvalidServerStateException"/>.
    /// </summary>
    /// <exception cref="InvalidServerStateException"></exception>
    void OnShutdown();
    /// <summary>
    /// Sets the server state to <c>Exiting</c> if the current state is <c>Starting</c> or <c>ShuttingDown</c>, otherwise throws an <see cref="InvalidServerStateException"/>.
    /// </summary>
    /// <exception cref="InvalidServerStateException"></exception>
    void OnExit();

    void OnTraceOff();
    void OnTraceMessages();
    void OnTraceVerbose();
}

internal class ServerStateProvider() : IServerStateProvider
{
    private ServerState _state = ServerState.Starting;
    public ServerState State => _state;

    public void OnInitialize() => _state = State is StartingServerState ? ServerState.Initializing : throw new InvalidServerStateException(State.Value);
    public void OnInitialized() => _state = State is InitializingServerState ? ServerState.Running : throw new InvalidServerStateException(State.Value);
    public void OnShutdown() => _state = State is RunningServerState ? ServerState.ShuttingDown : throw new InvalidServerStateException(State.Value);
    public void OnExit() => _state = State is StartingServerState or ShuttingDownServerState ? ServerState.Exiting : throw new InvalidServerStateException(State.Value);

    public void OnTraceOff()
    {
        _state = State is RunningServerState ? ServerState.RunningTraceless : throw new InvalidServerStateException(State.Value);
    }

    public void OnTraceMessages() => _state = State is RunningServerState ? ServerState.Running : throw new InvalidServerStateException(State.Value);
    public void OnTraceVerbose() => _state = State is RunningServerState ? ServerState.RunningVerbose : throw new InvalidServerStateException(State.Value);
}
