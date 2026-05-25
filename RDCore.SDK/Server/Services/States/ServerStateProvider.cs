using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Server.Configuration;

namespace RDCore.SDK.Server.Services.States;

/// <summary>
/// Defines methods for managing and transitioning the operational lifecycle state of a <c>ServerApp</c> instance.
/// </summary>
public interface IServerStateProvider
{
    /// <summary>
    /// Gets the current <c>ServerState</c>.
    /// </summary>
    ServerState State { get; }
    /// <summary>
    /// Gets a cancellation token that expires when a <c>Shutdown</c> request is handled.
    /// </summary>
    CancellationToken RequestToken { get; }
    /// <summary>
    /// Gets a cancellation token that expires when the <c>Exit</c> notification is handled.
    /// </summary>
    /// <remarks>
    /// <strong>This token terminates the server process.</strong>
    /// </remarks>
    CancellationToken ProcessToken { get; }
    /// <summary>
    /// Gets the server startup options the process was started with.
    /// </summary>
    ServerOptions Options { get; }
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
    /// Sets the server state to <c>ShuttingDown</c> if the current state is <c>Starting</c> or <c>Running</c>, otherwise throws an <see cref="InvalidServerStateException"/>;
    /// cancels the <c>RequestToken</c>.
    /// </summary>
    /// <exception cref="InvalidServerStateException"></exception>
    void OnShutdown();
    /// <summary>
    /// Sets the server state to <c>Exiting</c> if the current state is <c>Starting</c> or <c>ShuttingDown</c>, otherwise throws an <see cref="InvalidServerStateException"/>;
    /// cancels the <c>ProcessToken</c> (causes an immediate process exit).
    /// </summary>
    /// <exception cref="InvalidServerStateException"></exception>
    void OnExit();

    void OnTraceOff();
    void OnTraceMessages();
    void OnTraceVerbose();

    ServerInfo ServerInfo { get; }
}

/// <summary>
/// Manages the operational lifecycle state of a <c>ServerApp</c> instance.
/// </summary>
public class ServerStateProvider(ServerOptions options) : IServerStateProvider, IDisposable
{
    private static readonly CancellationTokenSource _processTokenSource = new();
    private static readonly CancellationTokenSource _requestTokenSource = new();
    private static readonly CancellationTokenSource _shutdownTimeoutTokenSource = new();

    private ServerState _state = ServerState.Starting;
    public ServerState State => _state;
    public ServerOptions Options { get; } = options;

    public CancellationToken RequestToken => _requestTokenSource.Token;
    public CancellationToken ProcessToken => _processTokenSource.Token;

    public void OnInitialize() => _state = State is StartingServerState ? ServerState.Initializing : throw new InvalidServerStateException(State.Value);
    public void OnInitialized() => _state = State is InitializingServerState ? ServerState.Running : throw new InvalidServerStateException(State.Value);
    public void OnShutdown()
    {
        _state = State is RunningServerState ? ServerState.ShuttingDown : throw new InvalidServerStateException(State.Value);
        _requestTokenSource.Cancel();
        _shutdownTimeoutTokenSource.CancelAfter(TimeSpan.FromSeconds(Options.ShutdownTimeoutSeconds));
    }

    public void OnExit()
    {
        _state = ServerState.Exiting(State.Value);
        _processTokenSource.Cancel();
    }

    public void OnTraceOff()
    {
        _state = State is RunningServerState ? ServerState.RunningTraceless : throw new InvalidServerStateException(State.Value);
    }

    public void OnTraceMessages() => _state = State is RunningServerState ? ServerState.Running : throw new InvalidServerStateException(State.Value);
    public void OnTraceVerbose() => _state = State is RunningServerState ? ServerState.RunningVerbose : throw new InvalidServerStateException(State.Value);

    public ServerInfo ServerInfo { get; } = new()
    {
        Name = ServerApp.Info.Name!,
        Version = ServerApp.Info.Version!.ToString(3)
    };


    public void Dispose()
    {
        _requestTokenSource.Dispose();
        _processTokenSource.Dispose();
        _shutdownTimeoutTokenSource.Dispose();
    }
}
