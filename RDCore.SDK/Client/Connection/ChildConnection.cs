using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Client;
using OmniSharp.Extensions.LanguageServer.Protocol.Client;
using OmniSharp.Extensions.LanguageServer.Shared;
using RDCore.SDK.Server;
using System.IO.Pipelines;
using System.IO.Pipes;

namespace RDCore.SDK.Client.Connection;

/// <summary>
/// Inputs for <see cref="ChildConnection.ConnectAsync"/>.
/// </summary>
public sealed record class ChildConnectionRequest
{
    /// <summary>The server executable to launch, relative to the platform root.</summary>
    public required string ServerExecutablePath { get; init; }
    /// <summary>The named pipe the child and this connection meet on.</summary>
    public required string PipeName { get; init; }
    /// <summary>Applies the app-specific parts of the <c>LanguageClient</c> configuration (client info, capabilities, lifecycle delegates, handlers, services).</summary>
    public required Action<LanguageClientOptions> ConfigureClient { get; init; }
    /// <summary>Invoked when the child is lost and cannot be restarted (attempts exhausted).</summary>
    public required Action OnPeerExited { get; init; }
    /// <summary>Seconds to wait for the transport connection before failing.</summary>
    public int ConnectTimeoutSeconds { get; init; } = 30;
    /// <summary>Restart attempts after an unexpected failure before escalating.</summary>
    public int MaxRestartAttempts { get; init; } = 3;
    /// <summary>Base restart delay in milliseconds; doubles per attempt, capped at <see cref="RestartBackoffMaxMs"/>.</summary>
    public int RestartBackoffBaseMs { get; init; } = 500;
    /// <summary>Maximum restart delay in milliseconds.</summary>
    public int RestartBackoffMaxMs { get; init; } = 10_000;
}

/// <summary>
/// Owns one supervised child process, its transport pipe, and its <c>LanguageClient</c>, and drives
/// them through the <see cref="ConnectionState"/> lifecycle, including restart-with-backoff.
/// </summary>
public sealed class ChildConnection(
    IRDCoreServerProcess serverProcess,
    ILanguageServerProtocolTransportLayer transportLayer,
    ILogger<ChildConnection> logger) : IDisposable
{
    private readonly CancellationTokenSource _connectionCts = new();
    private readonly object _gate = new();

    private ChildConnectionRequest? _request;
    private CancellationTokenSource? _linkedCts;
    private TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _terminated = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _restartCount;
    private NamedPipeClientStream? _pipe;
    private LanguageClient? _client;
    private volatile bool _shuttingDown;

    public ConnectionState State { get; private set; } = ConnectionState.NotStarted;

    /// <summary>The live language client. Throws until the connection is <see cref="ConnectionStateValue.Ready"/>.</summary>
    public ILanguageClient Client => _client ?? throw new InvalidOperationException($"The connection is {State.Value}, not Ready.");

    /// <summary>Raised on every state transition.</summary>
    public event Action<ConnectionState>? StateChanged;

    /// <summary>
    /// Drives the connection from <see cref="ConnectionStateValue.NotStarted"/> to
    /// <see cref="ConnectionStateValue.Ready"/>. Initial failures are not retried: the state becomes
    /// <see cref="ConnectionStateValue.Faulted"/> and the exception is rethrown.
    /// </summary>
    public async Task ConnectAsync(ChildConnectionRequest request, CancellationToken token)
    {
        _request = request;
        _linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, _connectionCts.Token);

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
    /// Completes once the connection is <see cref="ConnectionStateValue.Ready"/> (waiting through any
    /// in-progress restart); faults only once the connection is terminally <see cref="ConnectionStateValue.Exited"/>.
    /// </summary>
    public Task WaitForReadyAsync(CancellationToken token)
    {
        if (State.IsUsable) return Task.CompletedTask;
        if (State.IsTerminal) return Task.FromException(new ServerProtocolSdkException("The connection has exited."));
        return _ready.Task.WaitAsync(token);
    }

    public Task WaitForExitAsync() => serverProcess.WaitForExitAsync();

    /// <summary>Completes once the connection is terminally <see cref="ConnectionStateValue.Exited"/> (restart, if any, exhausted).</summary>
    public Task WaitForTerminalAsync() => _terminated.Task;

    public async Task<TResult> SendRequestAsync<TParams, TResult>(TParams request, CancellationToken token) where TParams : IRequest<TResult>
        => await Client.SendRequest(request, token);

    public Task SendNotificationAsync<TParams>(TParams notification, CancellationToken token) where TParams : IRequest
    {
        token.ThrowIfCancellationRequested();
        Client.SendNotification(notification);
        return Task.CompletedTask;
    }

    private async Task AttemptConnectAsync(CancellationToken ct)
    {
        Transition(ConnectionState.Spawning);
        await serverProcess.StartAsync(_request!.ServerExecutablePath, _request.PipeName, _connectionCts);

        Transition(ConnectionState.Connecting);
        _pipe?.Dispose();
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
        _client = await LanguageClient.From(ConfigureClientOptions, ct);

        Transition(ConnectionState.Ready);
    }

    private async Task MonitorPeerAsync()
    {
        while (true)
        {
            try { await serverProcess.WaitForExitAsync(); } catch { /* handled below */ }

            if (_shuttingDown || State.Value is ConnectionStateValue.Exited or ConnectionStateValue.ShuttingDown)
            {
                return;
            }

            Fault($"the child process exited unexpectedly (code {serverProcess.ExitCode})");

            if (await TryRestartAsync())
            {
                continue; // reconnected — monitor the new process
            }

            Transition(ConnectionState.Exited);
            _request?.OnPeerExited();
            return;
        }
    }

    private async Task<bool> TryRestartAsync()
    {
        // a fresh readiness gate for the restart window, so WaitForReadyAsync callers wait rather than fault.
        _ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // _restartCount is a lifetime counter: a child that keeps dying (even if each restart itself
        // succeeds) is given up on after MaxRestartAttempts, not restarted forever.
        while (_restartCount < _request!.MaxRestartAttempts)
        {
            var attempt = _restartCount++;
            var delayMs = RestartDelayMs(attempt, _request.RestartBackoffBaseMs, _request.RestartBackoffMaxMs);
            logger.LogWarning("Restarting child connection in {DelayMs} ms (attempt {Attempt}/{Max})", delayMs, attempt + 1, _request.MaxRestartAttempts);
            try
            {
                await Task.Delay(delayMs, _linkedCts!.Token);
                await AttemptConnectAsync(_linkedCts.Token);
                logger.LogInformation("Child connection restored ({Attempt} of {Max} lifetime restarts used)", attempt + 1, _request.MaxRestartAttempts);
                return true;
            }
            catch (OperationCanceledException)
            {
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

    /// <summary>Exponential backoff: <c>baseMs · 2^attempt</c>, capped at <paramref name="maxMs"/>.</summary>
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
        lock (_gate)
        {
            State = State.AdvanceTo(target);
        }
        logger.LogInformation("Connection state -> {State}", State.Value);
        if (State.IsUsable) _ready.TrySetResult();
        if (State.IsTerminal)
        {
            _ready.TrySetException(new ServerProtocolSdkException("The connection has exited."));
            _terminated.TrySetResult();
        }
        StateChanged?.Invoke(State);
    }

    private void Fault(string reason)
    {
        lock (_gate)
        {
            if (State.Value is ConnectionStateValue.Faulted) return;
            State = State.AdvanceTo(ConnectionState.Faulted(reason));
        }
        logger.LogWarning("Connection faulted: {Reason}", reason);
        StateChanged?.Invoke(State);
    }

    public void Dispose()
    {
        _shuttingDown = true;
        _connectionCts.Cancel();
        _connectionCts.Dispose();
        _linkedCts?.Dispose();
        _client?.Dispose();
        _pipe?.Dispose();
        serverProcess.Dispose();
    }
}
