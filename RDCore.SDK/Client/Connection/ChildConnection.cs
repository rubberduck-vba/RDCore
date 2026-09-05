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
    /// <summary>Invoked when the child exits unexpectedly (not during a graceful shutdown).</summary>
    public required Action OnPeerExited { get; init; }
    /// <summary>Seconds to wait for the transport connection before failing.</summary>
    public int ConnectTimeoutSeconds { get; init; } = 30;
}

/// <summary>
/// Owns one supervised child process, its transport pipe, and its <c>LanguageClient</c>, and drives
/// them through the <see cref="ConnectionState"/> lifecycle.
/// </summary>
public sealed class ChildConnection(
    IRDCoreServerProcess serverProcess,
    ILanguageServerProtocolTransportLayer transportLayer,
    ILogger<ChildConnection> logger) : IDisposable
{
    private readonly CancellationTokenSource _connectionCts = new();
    private readonly object _gate = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private ChildConnectionRequest? _request;
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
    /// <see cref="ConnectionStateValue.Ready"/>. On failure the state becomes
    /// <see cref="ConnectionStateValue.Faulted"/> and the exception is rethrown.
    /// </summary>
    public async Task ConnectAsync(ChildConnectionRequest request, CancellationToken token)
    {
        _request = request;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _connectionCts.Token);
        var ct = linked.Token;

        try
        {
            Transition(ConnectionState.Spawning);
            await serverProcess.StartAsync(request.ServerExecutablePath, request.PipeName, _connectionCts);

            Transition(ConnectionState.Connecting);
            _pipe = transportLayer.ConfigureClient(request.PipeName);
            var timeoutMs = (int)TimeSpan.FromSeconds(request.ConnectTimeoutSeconds > 0 ? request.ConnectTimeoutSeconds : 30).TotalMilliseconds;
            var connect = _pipe.ConnectAsync(timeoutMs, ct);
            if (await Task.WhenAny(connect, serverProcess.WaitForExitAsync()) != connect || serverProcess.HasExited)
            {
                throw new ServerProtocolSdkException("Child process exited before the transport connection was established.");
            }
            await connect;

            Transition(ConnectionState.Initializing);
            _client = await LanguageClient.From(ConfigureClientOptions, ct);

            Transition(ConnectionState.Ready);
            _ready.TrySetResult();

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
    /// Completes once the connection is <see cref="ConnectionStateValue.Ready"/>; faults if the
    /// connection reaches a terminal state first.
    /// </summary>
    public Task WaitForReadyAsync(CancellationToken token)
    {
        if (State.IsUsable) return Task.CompletedTask;
        if (State.Value is ConnectionStateValue.Exited or ConnectionStateValue.Faulted)
        {
            return Task.FromException(new ServerProtocolSdkException($"The connection is {State.Value}."));
        }
        return _ready.Task.WaitAsync(token);
    }

    public Task WaitForExitAsync() => serverProcess.WaitForExitAsync();

    public async Task<TResult> SendRequestAsync<TParams, TResult>(TParams request, CancellationToken token) where TParams : IRequest<TResult>
        => await Client.SendRequest(request, token);

    public Task SendNotificationAsync<TParams>(TParams notification, CancellationToken token) where TParams : IRequest
    {
        token.ThrowIfCancellationRequested();
        Client.SendNotification(notification);
        return Task.CompletedTask;
    }

    private async Task MonitorPeerAsync()
    {
        try { await serverProcess.WaitForExitAsync(); } catch { /* handled below */ }

        if (_shuttingDown || State.Value is ConnectionStateValue.Exited or ConnectionStateValue.ShuttingDown)
        {
            return;
        }

        // phase 1: a lost child is a terminal fault. Restart-with-backoff lands in phase 2.
        Fault("the child process exited unexpectedly");
        _request?.OnPeerExited();
    }

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
        _client?.Dispose();
        _pipe?.Dispose();
        serverProcess.Dispose();
    }
}
