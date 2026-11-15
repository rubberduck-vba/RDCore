using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using RDCore.SDK.Platform.Components;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server.Services;

namespace RDCore.LanguageServer.Debugging;

/// <summary>
/// The client of the language server, as the stops of the handlers of events that run while no program runs reach it.
/// </summary>
internal interface ISessionStopClient
{
    /// <summary>
    /// Whether the client said it takes those stops (<see cref="SDK.Client.SessionStops"/>).
    /// </summary>
    bool TakesStops { get; }

    /// <summary>
    /// Tells the client of a stop (<c>rdcore/session/debug/stopped</c>).
    /// </summary>
    /// <param name="notification">Where the handler waits.</param>
    /// <param name="token">A token that cancels the sending.</param>
    Task NotifyAsync(SessionDebugStoppedNotification notification, CancellationToken token);
}

/// <summary>
/// Relays the stops of the handlers of events that run in the environment host while no program runs (<c>rdcore/host/debug/stopped</c>) to whoever takes them: a
/// debug adapter that attached to the session, or else the client of the language server when it said it takes them.
/// </summary>
/// <remarks>
/// A stop nobody takes is answered as not taken, and the host stops the handler as it stops one that is not under a debugger. One that is taken waits in the host
/// until it is resumed, stepped or ended over <c>rdcore/session/debug/*</c>, as a program that waits is.
/// </remarks>
internal interface ISessionStopRelay
{
    /// <summary>
    /// Attaches the client, once the connection to it is live.
    /// </summary>
    /// <param name="client">The client.</param>
    void Attach(ISessionStopClient client);

    /// <summary>
    /// Has <paramref name="taker"/> take the stops until the registration is disposed, before the client: a debug adapter attached to the session.
    /// </summary>
    /// <param name="taker">Takes a stop, and answers whether it did.</param>
    /// <returns>What ends the registration.</returns>
    IDisposable Take(Func<ExecuteResult, bool> taker);

    /// <summary>
    /// Relays a stop from the host.
    /// </summary>
    /// <param name="request">Where the handler waits.</param>
    /// <param name="token">A token that cancels the relay.</param>
    /// <returns>Whether somebody took the stop.</returns>
    Task<HostDebugStoppedResult> StoppedAsync(HostDebugStoppedParams request, CancellationToken token);
}

internal sealed class SessionStopRelay(ILogger<SessionStopRelay>? logger = null) : ISessionStopRelay
{
    private readonly ILogger _logger = logger ?? NullLogger<SessionStopRelay>.Instance;
    private readonly Lock _sync = new();
    private ISessionStopClient? _client;
    private Func<ExecuteResult, bool>? _taker;

    public void Attach(ISessionStopClient client) => _client = client;

    public IDisposable Take(Func<ExecuteResult, bool> taker)
    {
        ArgumentNullException.ThrowIfNull(taker);
        lock (_sync)
        {
            _taker = taker;
        }

        return new Registration(() =>
        {
            lock (_sync)
            {
                if (ReferenceEquals(_taker, taker))
                {
                    _taker = null;
                }
            }
        });
    }

    public async Task<HostDebugStoppedResult> StoppedAsync(HostDebugStoppedParams request, CancellationToken token)
    {
        Func<ExecuteResult, bool>? taker;
        lock (_sync)
        {
            taker = _taker;
        }

        if (taker is not null)
        {
            return new HostDebugStoppedResult { Taken = taker(request.Stop) };
        }

        if (_client is not { TakesStops: true } client)
        {
            return new HostDebugStoppedResult();
        }

        try
        {
            await client.NotifyAsync(new SessionDebugStoppedNotification { Stop = request.Stop }, token);
            return new HostDebugStoppedResult { Taken = true };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "⚠️ The client could not be told that a handler stopped; the handler is stopped.");
            return new HostDebugStoppedResult();
        }
    }

    private sealed class Registration(Action dispose) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                dispose();
            }
        }
    }
}

/// <summary>
/// The client of a running language server: what it takes is what it said in the platform handshake.
/// </summary>
/// <param name="server">The language server, connected to its client.</param>
internal sealed class LanguageServerSessionStopClient(ILanguageServer server) : ISessionStopClient
{
    public bool TakesStops => server.Services.GetService<IPlatformClientCapabilitiesService>() is { } expected
        && expected.Expects(capabilities => capabilities.SessionStops);

    public Task NotifyAsync(SessionDebugStoppedNotification notification, CancellationToken token)
    {
        server.SendNotification(notification);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Handles <c>rdcore/host/debug/stopped</c>: a handler of an event stopped in the environment host while no program runs.
/// </summary>
internal sealed class HostDebugStoppedHandler(ISessionStopRelay relay) : IJsonRpcRequestHandler<HostDebugStoppedParams, HostDebugStoppedResult>
{
    public Task<HostDebugStoppedResult> Handle(HostDebugStoppedParams request, CancellationToken cancellationToken) => relay.StoppedAsync(request, cancellationToken);
}
