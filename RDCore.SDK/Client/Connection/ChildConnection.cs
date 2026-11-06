using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Client;
using OmniSharp.Extensions.LanguageServer.Protocol.Client;
using OmniSharp.Extensions.LanguageServer.Protocol.General;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server;
using System.IO.Pipelines;
using System.IO.Pipes;
using System.Reflection;

namespace RDCore.SDK.Client.Connection;

/// <summary>
/// Inputs for <see cref="ChildConnection.ConnectAsync"/>.
/// </summary>
public sealed record class ChildConnectionRequest
{
    /// <summary>
    /// The server executable to launch, relative to the platform root.
    /// </summary>
    public required string ServerExecutablePath { get; init; }

    /// <summary>
    /// The named pipe the child and this connection meet on.
    /// </summary>
    public required string PipeName { get; init; }

    /// <summary>
    /// Applies the app-specific parts of the <c>LanguageClient</c> configuration (client info,
    /// capabilities, lifecycle delegates, handlers, services).
    /// </summary>
    public required Action<LanguageClientOptions> ConfigureClient { get; init; }

    /// <summary>
    /// Invoked when the child is lost and cannot be restarted (attempts exhausted).
    /// </summary>
    public required Action OnPeerExited { get; init; }

    /// <summary>
    /// The mode the child is launched in when it is <c>rdc.exe</c> (<see cref="RDCoreServerProcess.HostMode"/>, <see cref="RDCoreServerProcess.ExternalMode"/>);
    /// <see langword="null"/> for any other server.
    /// </summary>
    public string? Mode { get; init; }

    /// <summary>
    /// The component the caller expects to be connecting to (sent in the platform handshake).
    /// </summary>
    public CoreServerComponent ExpectedComponent { get; init; }

    /// <summary>
    /// The platform capabilities the caller expects the child to provide.
    /// </summary>
    public CorePlatformClientCapabilities ExpectedCapabilities { get; init; } = new();

    /// <summary>
    /// Seconds to wait for the transport connection before failing.
    /// </summary>
    public int ConnectTimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// Restart attempts after an unexpected failure before escalating.
    /// </summary>
    public int MaxRestartAttempts { get; init; } = 3;

    /// <summary>
    /// Base restart delay in milliseconds; doubles per attempt, capped at <see cref="RestartBackoffMaxMs"/>.
    /// </summary>
    public int RestartBackoffBaseMs { get; init; } = 500;

    /// <summary>
    /// Maximum restart delay in milliseconds.
    /// </summary>
    public int RestartBackoffMaxMs { get; init; } = 10_000;

    /// <summary>
    /// Seconds to wait for the child to acknowledge a graceful <c>shutdown</c>/<c>exit</c> before it is killed.
    /// </summary>
    public int ShutdownTimeoutSeconds { get; init; } = 5;
}

/// <summary>
/// Owns one supervised child process, its transport pipe, and its <c>LanguageClient</c>, and drives
/// them through the <see cref="ConnectionState"/> lifecycle, including restart-with-backoff.
/// </summary>
/// <param name="serverProcess">The child process.</param>
/// <param name="transportLayer">Makes the pipe the connection meets the child on.</param>
/// <param name="logger">The logger.</param>
/// <param name="clock">The clock the delay before a restart is kept by; the system's when <see langword="null"/>.</param>
public sealed class ChildConnection(
    IRDCoreServerProcess serverProcess,
    ILanguageServerProtocolTransportLayer transportLayer,
    ILogger<ChildConnection> logger,
    TimeProvider? clock = null) : IDisposable
{
    private readonly CancellationTokenSource _connectionCts = new();
    // cancels a restart that is under way when the connection is shut down. It has no timer, so it is not disposed: a shutdown that races Dispose
    // cancels it all the same.
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly object _gate = new();

    private ChildConnectionRequest? _request;
    private CancellationTokenSource? _linkedCts;
    private TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _terminated = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _restartCount;
    private NamedPipeClientStream? _pipe;
    private LanguageClient? _client;
    private OmniSharpPlatformChannel? _channel;
    // completes when the process _client is connected to exits: a request sent to it and not answered by then is never answered.
    private TaskCompletionSource _lost = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile bool _shuttingDown;
    private bool _disposed;

    /// <summary>
    /// The operating-system identifier of the child process; <c>0</c> before it is started.
    /// </summary>
    public int ProcessId => serverProcess.ProcessId;

    /// <summary>
    /// The current lifecycle state of the connection.
    /// </summary>
    public ConnectionState State { get; private set; } = ConnectionState.NotStarted;

    /// <summary>
    /// The result of the <c>rdcore/platform/initialize</c> handshake, once the connection is Ready.
    /// </summary>
    public PlatformInitializeResult? PlatformInfo { get; private set; }

    /// <summary>
    /// The live language client. Throws until the connection is <see cref="ConnectionStateValue.Ready"/>.
    /// </summary>
    public ILanguageClient Client => _client ?? throw new InvalidOperationException($"The connection is {State.Value}, not Ready.");

    /// <summary>
    /// The live language client as a channel of the platform protocol (<see cref="RDCorePlatformClient"/>). Throws until the connection is
    /// <see cref="ConnectionStateValue.Ready"/>.
    /// </summary>
    public IRDCorePlatformChannel Channel => _channel ?? throw new InvalidOperationException($"The connection is {State.Value}, not Ready.");

    /// <summary>
    /// Raised on every state transition.
    /// </summary>
    public event Action<ConnectionState>? StateChanged;

    /// <summary>
    /// Drives the connection from <see cref="ConnectionStateValue.NotStarted"/> to
    /// <see cref="ConnectionStateValue.Ready"/>. Initial failures are not retried: the state becomes
    /// <see cref="ConnectionStateValue.Faulted"/> and the exception is rethrown.
    /// </summary>
    /// <param name="request">The connection inputs.</param>
    /// <param name="token">Cancels the connection attempt.</param>
    public async Task ConnectAsync(ChildConnectionRequest request, CancellationToken token)
    {
        _request = request;
        _linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, _connectionCts.Token, _shutdownCts.Token);

        try
        {
            await AttemptConnectAsync(_linkedCts.Token);
            _ = MonitorPeerAsync();
        }
        catch (Exception exception)
        {
            Fault(exception.Message);
            _ready.TrySetException(exception);
            throw;
        }
    }

    /// <summary>
    /// Completes once the connection is <see cref="ConnectionStateValue.Ready"/> (waiting through any in-progress restart); faults once the connection is
    /// terminally <see cref="ConnectionStateValue.Exited"/>, is shutting down, or is disposed.
    /// </summary>
    /// <param name="token">Cancels the wait.</param>
    /// <exception cref="ServerProtocolSdkException">The connection has exited, or is shutting down.</exception>
    /// <exception cref="ObjectDisposedException">The connection is disposed, or is disposed while the wait waits.</exception>
    /// <remarks>
    /// A connection whose child process has exited is not Ready from the moment its exit is seen, before its state says so: what completes this wait is the
    /// process a request sent next goes to, and not the one that a request in flight was lost with.
    /// </remarks>
    public Task WaitForReadyAsync(CancellationToken token)
    {
        Task ready;
        lock (_gate)
        {
            if (_disposed)
            {
                return Task.FromException(new ObjectDisposedException(nameof(ChildConnection)));
            }
            if (State.IsTerminal)
            {
                return Task.FromException(new ServerProtocolSdkException("The connection has exited."));
            }
            if (_shuttingDown || State.Value is ConnectionStateValue.ShuttingDown)
            {
                return Task.FromException(new ServerProtocolSdkException("The connection is shutting down."));
            }
            if (State.IsUsable && !_lost.Task.IsCompleted)
            {
                return Task.CompletedTask;
            }
            ready = _ready.Task;
        }
        return ready.WaitAsync(token);
    }

    /// <summary>
    /// Completes when the child process exits.
    /// </summary>
    public Task WaitForExitAsync() => serverProcess.WaitForExitAsync();

    /// <summary>
    /// Completes once the connection is terminally <see cref="ConnectionStateValue.Exited"/> (restart, if any, exhausted).
    /// </summary>
    public Task WaitForTerminalAsync() => _terminated.Task;

    /// <summary>
    /// Graceful teardown: LSP <c>shutdown</c> request, then <c>exit</c> notification, then a bounded wait
    /// for the process, then a kill. Suppresses restart. Idempotent.
    /// </summary>
    public async Task ShutdownAsync()
    {
        lock (_gate)
        {
            if (_shuttingDown || State.IsTerminal)
            {
                return;
            }
            _shuttingDown = true;
        }

        // a restart under way is stopped where it waits; one past its last wait sees the shutdown before it spawns, or shuts down what it spawned.
        _shutdownCts.Cancel();

        if (State.Value is ConnectionStateValue.Ready && _client is ILanguageClient client)
        {
            Transition(ConnectionState.ShuttingDown);
            var timeoutSeconds = _request?.ShutdownTimeoutSeconds ?? 5;
            var timeout = TimeSpan.FromSeconds(timeoutSeconds > 0 ? timeoutSeconds : 5);

            // drive the LSP shutdown/exit handshake directly rather than LanguageClient.Shutdown(),
            // which also stops and disposes the connection and races the in-flight response to an
            // "Internal error." a shared-console Ctrl+C also hits the child directly, so any step here
            // may fail because the peer is already gone — each is bounded and non-fatal; the kill
            // fallback below is the backstop.
            try
            {
                await client.RequestShutdown().WaitAsync(timeout);
            }
            catch (Exception exception)
            {
                logger.LogDebug("Child did not acknowledge shutdown ({Message}).", exception.Message);
            }

            try
            {
                client.SendExit();
            }
            catch (Exception exception)
            {
                logger.LogDebug("Could not send exit to the child ({Message}).", exception.Message);
            }

            try
            {
                await serverProcess.WaitForExitAsync().WaitAsync(timeout);
            }
            catch (TimeoutException)
            {
                logger.LogWarning("Child did not exit within {Timeout}s of the shutdown handshake; killing it.", timeout.TotalSeconds);
            }
            catch (Exception exception)
            {
                logger.LogDebug("Wait for child exit ended ({Message}).", exception.Message);
            }
        }

        if (!serverProcess.HasExited)
        {
            serverProcess.Shutdown();
        }
        ToTerminal();
    }

    private void ToTerminal()
    {
        if (State.IsTerminal)
        {
            return;
        }
        if (State.Value is not ConnectionStateValue.ShuttingDown and not ConnectionStateValue.Faulted)
        {
            Transition(ConnectionState.Faulted("connection closed"));
        }
        Transition(ConnectionState.Exited);
    }

    /// <summary>
    /// Sends a request over the connection.
    /// </summary>
    /// <typeparam name="TParams">The request parameter type.</typeparam>
    /// <typeparam name="TResult">The response type.</typeparam>
    /// <param name="request">The request parameters.</param>
    /// <param name="token">Cancels the request.</param>
    /// <exception cref="ServerProtocolSdkException">The child process exited before it answered: a connection that restarts does not send the request again.</exception>
    public async Task<TResult> SendRequestAsync<TParams, TResult>(TParams request, CancellationToken token) where TParams : IRequest<TResult>
    {
        var method = typeof(TParams).GetCustomAttribute<OmniSharp.Extensions.JsonRpc.MethodAttribute>()?.Method ?? typeof(TParams).Name;
        logger.LogInformation("→ {method} ({component})", method, _request?.ExpectedComponent);
        try
        {
            var (client, lost) = Incarnation();
            var result = await RequestsInFlight.AnsweredAsync(client.SendRequest(request, token), lost, method);
            logger.LogInformation("← {method} completed", method);
            return result;
        }
        catch (Exception exception) when (token.IsCancellationRequested)
        {
            // a request the caller cancelled is one it does not want the answer to any more - a completion list for a word that is not being typed now - and
            // not one that failed: the caller is told, and nothing is wrong.
            logger.LogDebug(exception, "← {method} cancelled", method);
            throw;
        }
        catch (OmniSharp.Extensions.JsonRpc.Server.JsonRpcException exception) when (exception.ErrorCode is RequestFailedCode or ContentModifiedCode)
        {
            // a request the server refused (a rename to a keyword), or that an edit overtook, is answered: the caller is told why, and decides what to say.
            logger.LogDebug("← {method} answered {code}: {message}", method, exception.ErrorCode, exception.Message);
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "← {method} faulted", method);
            throw;
        }
    }

    // LSP 3.17 ErrorCodes: the request was valid and refused; the content it was about changed while it was answered.
    private const int RequestFailedCode = -32803;
    private const int ContentModifiedCode = -32801;

    // the client and the exit of the process it is connected to, as one: a request is sent to the process whose exit it waits for.
    private (ILanguageClient Client, Task Lost) Incarnation()
    {
        lock (_gate)
        {
            return (Client, _lost.Task);
        }
    }

    /// <summary>
    /// Sends a notification over the connection.
    /// </summary>
    /// <typeparam name="TParams">The notification parameter type.</typeparam>
    /// <param name="notification">The notification parameters.</param>
    /// <param name="token">Cancels before the notification is sent.</param>
    public Task SendNotificationAsync<TParams>(TParams notification, CancellationToken token) where TParams : IRequest
    {
        token.ThrowIfCancellationRequested();
        Client.SendNotification(notification);
        return Task.CompletedTask;
    }

    private async Task AttemptConnectAsync(CancellationToken ct)
    {
        Transition(ConnectionState.Spawning);
        await serverProcess.StartAsync(_request!.ServerExecutablePath, _request.PipeName, _connectionCts, _request.Mode);
        if (_shuttingDown)
        {
            // the connection was shut down while the process started: what started is not the connection's to keep.
            serverProcess.Shutdown();
            throw new OperationCanceledException("The connection was shut down while the child process started.");
        }

        Transition(ConnectionState.Connecting);
        if (_pipe is not null)
        {
            await _pipe.DisposeAsync();
        }
        _pipe = transportLayer.ConfigureClient(_request.PipeName);
        var timeoutMs = (int)TimeSpan.FromSeconds(_request.ConnectTimeoutSeconds > 0 ? _request.ConnectTimeoutSeconds : 30).TotalMilliseconds;
        var connect = _pipe.ConnectAsync(timeoutMs, ct);
        if (await Task.WhenAny(connect, serverProcess.WaitForExitAsync()) != connect || serverProcess.HasExited)
        {
            throw new ServerProtocolSdkException("Child process exited before the transport connection was established.");
        }
        await connect;

        Transition(ConnectionState.Initializing);
        _client?.Dispose();
        var lost = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var client = LanguageClient.Create(ConfigureClientOptions);
            try
            {
                await BeforeTheChildExitsAsync(async token =>
                {
                    await client.Initialize(token);
                    return true;
                }, "it was initialized", ct);
            }
            catch
            {
                // a client whose initialization was abandoned or failed is not kept: it would hold the pipe it reads.
                client.Dispose();
                throw;
            }
            OmniSharpPlatformChannel channel;
            lock (_gate)
            {
                _client = client;
                _lost = lost;
                _channel = channel = new OmniSharpPlatformChannel(client, logger, _request.ExpectedComponent.ToString(), lost.Task);
            }

            // non-LSP platform handshake: the child's advertised capabilities become "provided" ones.
            PlatformInfo = await BeforeTheChildExitsAsync(
                token => new RDCorePlatformClient(channel).InitializeAsync(_request.ExpectedComponent, _request.ExpectedCapabilities, token), "the platform handshake", ct);
        }
        catch
        {
            // a request sent to this process while it was initialized is answered by it or not at all: it is not, now.
            lost.TrySetResult();
            throw;
        }

        logger.LogInformation("Platform handshake with {Component}: provides {Provided}",
            PlatformInfo.Component, PlatformInfo.Provided);

        Transition(ConnectionState.Ready);
    }

    // what only the child answers: when the child exits first, nothing ever will, and the attempt fails as the child has gone. What was asked is abandoned,
    // and whatever becomes of it is observed here.
    private async Task<T> BeforeTheChildExitsAsync<T>(Func<CancellationToken, Task<T>> ask, string before, CancellationToken ct)
    {
        using var abandon = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var exited = serverProcess.WaitForExitAsync();
        var answer = ask(abandon.Token);
        if (await Task.WhenAny(answer, exited) == answer)
        {
            return await answer;
        }

        abandon.Cancel();
        _ = answer.ContinueWith(static abandoned => _ = abandoned.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        throw new ServerProtocolSdkException($"Child process exited before {before}.");
    }

    private async Task MonitorPeerAsync()
    {
        try
        {
            await MonitorPeerLoopAsync();
        }
        catch (Exception exception)
        {
            // this runs as a fire-and-forget task: an unobserved throw here would silently strand the
            // platform. Escalate instead so the owner tears down.
            logger.LogError(exception, "Peer monitor failed unexpectedly; escalating.");
            _request?.OnPeerExited();
        }
    }

    private async Task MonitorPeerLoopAsync()
    {
        while (true)
        {
            try
            {
                await serverProcess.WaitForExitAsync();
            }
            catch
            {
                // the exit reason is inspected below via serverProcess.ExitCode.
            }

            // what the process was asked and did not answer is lost with it, whether the connection restarts or not; and from then on, the connection is ready
            // only once the next process is. Both at once: whoever is told its request was lost and waits for the connection waits for the next process.
            lock (_gate)
            {
                _lost.TrySetResult();
                if (!State.IsTerminal && _ready.Task.IsCompletedSuccessfully)
                {
                    _ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                }
            }

            if (_shuttingDown || State.Value is ConnectionStateValue.Exited or ConnectionStateValue.ShuttingDown)
            {
                return;
            }

            // exit code 0 is a deliberate stop (graceful shutdown, or the peer escalating a fatal
            // condition it cannot recover from) — following it down, not restarting it, is correct.
            if (serverProcess.ExitCode == 0)
            {
                logger.LogInformation("Child exited cleanly (code 0); treating as a deliberate peer shutdown.");
                Transition(ConnectionState.ShuttingDown);
                Transition(ConnectionState.Exited);
                _request?.OnPeerExited();
                return;
            }

            Fault($"the child process exited unexpectedly (code {serverProcess.ExitCode})");

            if (await TryRestartAsync())
            {
                continue; // reconnected — monitor the new process
            }

            if (_shuttingDown)
            {
                // the shutdown that stopped the restart ends the connection: the owner asked for it, and is not told the child went down.
                logger.LogInformation("The connection is shutting down: the child is not restarted.");
                return;
            }

            Transition(ConnectionState.Exited);
            _request?.OnPeerExited();
            return;
        }
    }

    private async Task<bool> TryRestartAsync()
    {
        // _restartCount is a lifetime counter: a child that keeps dying (even if each restart itself
        // succeeds) is given up on after MaxRestartAttempts, not restarted forever.
        while (_restartCount < _request!.MaxRestartAttempts && !_shuttingDown)
        {
            var attempt = _restartCount++;
            var delayMs = RestartDelayMs(attempt, _request.RestartBackoffBaseMs, _request.RestartBackoffMaxMs);
            logger.LogWarning("Restarting child connection in {DelayMs} ms (attempt {Attempt}/{Max})", delayMs, attempt + 1, _request.MaxRestartAttempts);
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(delayMs), _clock, _linkedCts!.Token);
                if (_shuttingDown)
                {
                    return false;
                }
                await AttemptConnectAsync(_linkedCts.Token);
                logger.LogInformation("Child connection restored ({Attempt} of {Max} lifetime restarts used)", attempt + 1, _request.MaxRestartAttempts);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception) when (_shuttingDown)
            {
                // an attempt the shutdown cut short did not fail: it was stopped.
                return false;
            }
            catch (Exception exception)
            {
                logger.LogWarning("Restart attempt {Attempt} failed: {Message}", attempt + 1, exception.Message);
                Fault(exception.Message);
            }
        }
        return false;
    }

    /// <summary>
    /// Exponential backoff: <c>baseMs · 2^attempt</c>, capped at <paramref name="maxMs"/>.
    /// </summary>
    /// <param name="attempt">The zero-based restart attempt.</param>
    /// <param name="baseMs">The base delay in milliseconds.</param>
    /// <param name="maxMs">The maximum delay in milliseconds.</param>
    internal static int RestartDelayMs(int attempt, int baseMs, int maxMs)
        => (int)Math.Min(maxMs, (long)baseMs << attempt);

    private void ConfigureClientOptions(LanguageClientOptions options)
    {
        options
            .WithInput(PipeReader.Create(_pipe!))
            .WithOutput(PipeWriter.Create(_pipe!));
        options.Services.AddSingleton<ILanguageClientFacade>(_ => _client!);
        _request!.ConfigureClient(options);
    }

    private void Transition(ConnectionState target)
    {
        TaskCompletionSource ready;
        ConnectionState state;
        lock (_gate)
        {
            State = state = State.AdvanceTo(target);
            ready = _ready;
        }
        logger.LogInformation("Connection state -> {State}", state.Value);
        if (state.IsUsable)
        {
            ready.TrySetResult();
        }
        if (state.IsTerminal)
        {
            ready.TrySetException(new ServerProtocolSdkException("The connection has exited."));
            _terminated.TrySetResult();
        }
        StateChanged?.Invoke(state);
    }

    private void Fault(string reason)
    {
        ConnectionState state;
        lock (_gate)
        {
            if (State.Value is ConnectionStateValue.Faulted)
            {
                return;
            }
            State = state = State.AdvanceTo(ConnectionState.Faulted(reason));
        }
        logger.LogWarning("Connection faulted: {Reason}", reason);
        StateChanged?.Invoke(state);
    }

    /// <summary>
    /// Cancels any in-flight connection work and disposes the process, pipe, and client.
    /// </summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _shuttingDown = true;

            // a connection disposed during a restart is never ready: whoever waits for it is told. Whoever waits after this is told at once
            // (WaitForReadyAsync), whatever gate an exit seen after it makes.
            _ready.TrySetException(new ObjectDisposedException(nameof(ChildConnection)));
        }

        if (!_connectionCts.IsCancellationRequested)
        {
            _connectionCts.Cancel();
        }
        _connectionCts.Dispose();
        _linkedCts?.Dispose();
        _client?.Dispose();
        _pipe?.Dispose();
        serverProcess.Dispose();
    }
}
