using Microsoft.Extensions.Logging;
using RDCore.External.Protocol;
using RDCore.SDK.Client;
using RDCore.SDK.Client.Connection;
using RDCore.SDK.Platform.Channels;
using System.IO.Pipes;

namespace RDCore.External.Client;

/// <summary>
/// The external host of an environment host: <c>rdc</c> in external mode, started the first time a program reaches out, and started again the next time after it stopped.
/// </summary>
/// <remarks>
/// <para>
/// One to an environment host, as the machine's servers are one: the objects a program made outlive the program in its session, and are let go of by what made them.
/// </para>
/// <para>
/// A process that stops takes what it held with it. The objects it held are gone, and a program that uses one is told the server it was the object of is not available,
/// as MS-VBA tells a program whose out-of-process server stopped; the call it was making when it stopped, if any, failed the same way. The next call that needs the
/// external host starts another one - an <em>incarnation</em> of its own, whose objects are never mistaken for the last one's.
/// </para>
/// </remarks>
public sealed class ExternalHost : IDisposable
{
    private readonly Func<ChildConnection> _connections;
    private readonly string _executable;
    private readonly ILogger _logger;
    private readonly object _gate = new();
    private Incarnation? _current;
    private int _incarnations;
    private bool _disposed;

    /// <summary>
    /// Creates the external host of an environment host; nothing is started until a call needs it.
    /// </summary>
    /// <param name="connections">Creates the connection to a process, and the process.</param>
    /// <param name="executable">The executable the external host is: <c>rdc</c> (<see cref="DefaultExecutable"/>).</param>
    /// <param name="logger">A standard logger.</param>
    public ExternalHost(Func<ChildConnection> connections, string executable, ILogger logger)
    {
        _connections = connections;
        _executable = executable;
        _logger = logger;
    }

    /// <summary>
    /// The <c>rdc</c> executable next to the assemblies of this process: the environment host is <c>rdc</c> too, and the external host is the same executable, run in
    /// another mode.
    /// </summary>
    public static string DefaultExecutable => Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "rdc.exe" : "rdc");

    /// <summary>
    /// What handles an event that an incarnation's servers raised; set by the client that listens to them.
    /// </summary>
    internal Func<Incarnation, AutomationEventParams, AutomationEventResult>? AutomationEvent { get; set; }

    /// <summary>
    /// The incarnation that is running, started if none is.
    /// </summary>
    /// <exception cref="ExternalHostUnavailableException">The external host could not be started.</exception>
    internal Incarnation Connect()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_current is { IsLost: false } running)
            {
                return running;
            }

            var connection = _connections();
            var incarnation = new Incarnation(++_incarnations, connection);
            var pipeName = $"RDCore.{CoreServerComponent.ExternalHost}.Pipe.{Random.Shared.NextInt64()}";
            try
            {
                connection.ConnectAsync(new ChildConnectionRequest
                {
                    ServerExecutablePath = _executable,
                    PipeName = pipeName,
                    Mode = RDCoreServerProcess.ExternalMode,
                    ExpectedComponent = CoreServerComponent.ExternalHost,
                    // a process that stopped is not started again behind the program's back: what it held is gone, and the next call starts a new one.
                    MaxRestartAttempts = 0,
                    ConfigureClient = _ => { },
                    OnPeerExited = incarnation.MarkLost,
                }, CancellationToken.None).GetAwaiter().GetResult();

                // the calls go over a pipe of their own, which the process opened before it said it was up.
                var calls = new NamedPipeClientStream(".", ExternalProtocol.CallsPipeName(pipeName), PipeDirection.InOut, PipeOptions.CurrentUserOnly | PipeOptions.Asynchronous);
                calls.Connect(TimeSpan.FromSeconds(30));
                incarnation.Attach(new CallChannel(calls), raised => AutomationEvent is { } handle ? handle(incarnation, raised) : new AutomationEventResult { Arguments = raised.Arguments });

                incarnation.IsAutomationAvailable = incarnation.Send<AutomationStatusParams, AutomationStatusResult>(ExternalProtocol.AutomationStatus, new AutomationStatusParams()).IsAvailable;
            }
            catch (Exception exception) when (exception is not ExternalHostUnavailableException)
            {
                _logger.LogWarning(exception, "🔌 The external host could not be started.");
                incarnation.MarkLost();
                connection.Dispose();
                throw new ExternalHostUnavailableException(exception);
            }

            _logger.LogInformation("🔌 External host #{Incarnation} is up.", incarnation.Number);
            _current = incarnation;
            return incarnation;
        }
    }

    /// <summary>
    /// The incarnation that is running, started if none is; <see langword="false"/> when none could be.
    /// </summary>
    internal bool TryConnect(out Incarnation? incarnation)
    {
        try
        {
            incarnation = Connect();
            return true;
        }
        catch (ExternalHostUnavailableException)
        {
            incarnation = null;
            return false;
        }
    }

    /// <summary>
    /// Stops the incarnation that is running, if one is.
    /// </summary>
    public void Dispose()
    {
        Incarnation? stopping;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            stopping = _current;
            _current = null;
        }

        if (stopping is not null)
        {
            stopping.MarkLost();
            stopping.Calls?.Dispose();
            _ = stopping.Connection.ShutdownAsync().Wait(TimeSpan.FromSeconds(5));
            stopping.Connection.Dispose();
        }
    }
}

/// <summary>
/// One process of an external host, from when it was started to when it stopped.
/// </summary>
/// <param name="number">Which incarnation it is: the first is <c>1</c>.</param>
/// <param name="connection">The connection to the process.</param>
internal sealed class Incarnation(int number, ChildConnection connection)
{
    private readonly TaskCompletionSource _lost = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Which incarnation it is.
    /// </summary>
    public int Number { get; } = number;

    /// <summary>
    /// The connection to the process: what started it, and says when it stops.
    /// </summary>
    public ChildConnection Connection { get; } = connection;

    /// <summary>
    /// The channel the calls go over, once it is attached.
    /// </summary>
    public CallChannel? Calls { get; private set; }

    /// <summary>
    /// Attaches the channel the calls go over, and starts it: the process is lost when the channel closes, which it does as soon as the process stops.
    /// </summary>
    /// <param name="calls">The channel.</param>
    /// <param name="onEvent">Handles an event the process raises.</param>
    public void Attach(CallChannel calls, Func<AutomationEventParams, AutomationEventResult> onEvent)
    {
        Calls = calls;
        calls.Handle(ExternalProtocol.AutomationEvent, onEvent);
        calls.Start();
        _ = calls.Closed.ContinueWith(_ => MarkLost(), TaskScheduler.Default);
    }

    /// <summary>
    /// Whether the process has stopped.
    /// </summary>
    public bool IsLost => _lost.Task.IsCompleted;

    /// <summary>
    /// Whether the process has automation servers to reach.
    /// </summary>
    public bool IsAutomationAvailable { get; set; }

    /// <summary>
    /// Says the process has stopped.
    /// </summary>
    public void MarkLost() => _lost.TrySetResult();

    /// <summary>
    /// Sends a request, and waits for the answer - or for the process to stop, which closes the channel and is an answer too.
    /// </summary>
    /// <param name="method">The method the request calls.</param>
    /// <param name="request">What it is called with.</param>
    /// <exception cref="ExternalHostLostException">The process stopped before it answered.</exception>
    /// <exception cref="CallChannelException">The process failed to answer.</exception>
    public TResult Send<TParams, TResult>(string method, TParams request)
    {
        if (IsLost || Calls is not { } calls)
        {
            throw new ExternalHostLostException();
        }

        try
        {
            return calls.Call<TParams, TResult>(method, request);
        }
        catch (CallChannelException) when (calls.Closed.IsCompleted)
        {
            MarkLost();
            throw new ExternalHostLostException();
        }
    }
}

/// <summary>
/// The external host stopped before it answered a request.
/// </summary>
internal sealed class ExternalHostLostException() : Exception(ExternalMessages.HostStopped);

/// <summary>
/// The external host could not be started.
/// </summary>
/// <param name="inner">Why.</param>
internal sealed class ExternalHostUnavailableException(Exception inner) : Exception(ExternalMessages.HostUnavailable, inner);
