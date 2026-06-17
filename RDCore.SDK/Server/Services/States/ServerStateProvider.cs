using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
/// <remarks>
/// 👉 This class works with <see cref="IConfiguration"/> rather than <see cref="IOptions{T}"/> 
/// because it must be created before the application is fully configured, and must also <strong>survive the destruction</strong> of the application host in case of a hard crash.
/// </remarks>
public sealed class ServerStateProvider(IConfiguration configuration) : IServerStateProvider, IDisposable
{
    private readonly CancellationTokenSource _processTokenSource = new();
    private readonly CancellationTokenSource _requestTokenSource = new();
    private readonly CancellationTokenSource _shutdownTimeoutTokenSource = new();

    private ServerState _state = ServerState.Starting;
    public ServerState State => _state;

    public CancellationTokenSource ShutdownRequestTokenSource => _requestTokenSource;
    public CancellationTokenSource ProcessTokenSource => _processTokenSource;

    public void OnInitialize() => _state = GetValidStateOrThrow(State, typeof(StartingServerState), ServerState.Initializing);
    public void OnInitialized() => _state = GetValidStateOrThrow(State, typeof(InitializingServerState),
        Enum.Parse<LogLevel>(configuration["Server.TraceLevel"]!) == LogLevel.None
            ? ServerState.RunningTraceless
            : Convert.ToBoolean(configuration["Server:Verbose"])
                ? ServerState.RunningVerbose
                : ServerState.Running);

    public void OnShutdown()
    {
        _state = GetValidStateOrThrow(State, typeof(RunningServerState), ServerState.ShuttingDown);
        _requestTokenSource.Cancel();
        _shutdownTimeoutTokenSource.CancelAfter(TimeSpan.FromSeconds(Convert.ToInt32(configuration["Server:ShutdownTimeoutSeconds"])));
    }

    public void OnExit()
    {
        _state = ServerState.Exiting(State.Value);
        _processTokenSource.Cancel();
    }

    private static ServerState GetValidStateOrThrow(ServerState currentState, Type condition, ServerState validState) 
        => currentState.GetType().Equals(condition) ? validState : throw new InvalidServerStateException(currentState.Value);

    public void OnTraceOff() => _state = GetValidStateOrThrow(State, typeof(RunningServerState), ServerState.RunningTraceless);
    public void OnTraceMessages() => _state = GetValidStateOrThrow(State, typeof(RunningServerState), ServerState.Running);
    public void OnTraceVerbose() => _state = GetValidStateOrThrow(State, typeof(RunningServerState), ServerState.RunningVerbose);

    public void Dispose()
    {
        _requestTokenSource.Dispose();
        _processTokenSource.Dispose();
        _shutdownTimeoutTokenSource.Dispose();
    }
}
