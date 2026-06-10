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
    /// Gets a <see cref="CancellationTokenSource"/> that expires when a <c>Shutdown</c> request is handled.
    /// </summary>
    CancellationTokenSource ShutdownRequestTokenSource { get; }
    /// <summary>
    /// Gets the <see cref="CancellationTokenSource"/> that expires when an <c>Exit</c> notification is handled.
    /// </summary>
    /// <remarks>
    /// ⚠️ This token <strong>terminates</strong> the server process. The process should exit with the current <c>ExitCode</c>.
    /// </remarks>
    CancellationTokenSource ProcessTokenSource { get; }
    /// <summary>
    /// Sets the server state to <see cref="InitializingServerState"/> if the current state is <see cref="StartingServerState"/>, otherwise throws an <see cref="InvalidServerStateException"/>.
    /// </summary>
    /// <exception cref="InvalidServerStateException"></exception>
    void OnInitialize();
    /// <summary>
    /// Sets the server state to <see cref="RunningServerState"/> if the current state is <see cref="InitializingServerState"/>, otherwise throws an <see cref="InvalidServerStateException"/>.
    /// </summary>
    /// <exception cref="InvalidServerStateException"></exception>
    void OnInitialized();
    /// <summary>
    /// Sets the server state to <c>ShuttingDown</c> if the current state is <see cref="StartingServerState"/> or <see cref="RunningServerState"/>, 
    /// otherwise throws an <see cref="InvalidServerStateException"/>; cancels the <c>RequestToken</c>.
    /// </summary>
    /// <exception cref="InvalidServerStateException"></exception>
    void OnShutdown();
    /// <summary>
    /// Sets the server state to <see cref="ExitingServerState"/> if the current state is <see cref="StartingServerState"/> or <c>ShuttingDown</c>, otherwise throws an <see cref="InvalidServerStateException"/>;
    /// cancels the <c>ProcessToken</c> (causes an immediate process exit).
    /// </summary>
    /// <exception cref="InvalidServerStateException"></exception>
    void OnExit();
    /// <summary>
    /// Sets the server state to <see cref="RunningTracelessServerState"/>.
    /// </summary>
    void OnTraceOff();
    /// <summary>
    /// Sets the server state to <see cref="RunningServerState"/> with tracing enabled.
    /// </summary>
    void OnTraceMessages();
    /// <summary>
    /// Sets the server state to <see cref="RunningServerState"/> with <em>verbose</em> tracing enabled.
    /// </summary>
    void OnTraceVerbose();
}

/// <summary>
/// Manages the operational lifecycle state of a <c>ServerApp</c> instance.
/// </summary>
public class ServerStateProvider(SdkServerOptions options) : IServerStateProvider, IDisposable
{
    private static readonly CancellationTokenSource _processTokenSource = new();
    private static readonly CancellationTokenSource _requestTokenSource = new();
    private static readonly CancellationTokenSource _shutdownTimeoutTokenSource = new();

    private ServerState _state = ServerState.Starting;
    public ServerState State => _state;
    public SdkServerOptions Options { get; } = options;

    public CancellationTokenSource ShutdownRequestTokenSource => _requestTokenSource;
    public CancellationTokenSource ProcessTokenSource => _processTokenSource;

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
        Name = AppHost<ILanguageServerApp>.Info.Name!,
        Version = AppHost<ILanguageServerApp>.Info.Version!.ToString(3)
    };


    public void Dispose()
    {
        _requestTokenSource.Dispose();
        _processTokenSource.Dispose();
        _shutdownTimeoutTokenSource.Dispose();
    }
}
