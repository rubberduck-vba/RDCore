using Microsoft.Extensions.Logging;
using RDCore.SDK.Server.Services.States;
using System.Diagnostics;

namespace RDCore.SDK.Server.Services;

public interface IHealthCheckService : IDisposable
{
    void Start(long? clientProcessId);
    void Pause();
    void Resume();
}

public sealed class HealthCheckService : IHealthCheckService
{
    private readonly Timer _timer;
    private readonly ILogger _logger;

    private readonly IServerStateProvider _serverState;

    private TimeSpan _interval;

    private bool _didNotify;
    private Process? _process;

    public HealthCheckService(ILogger<HealthCheckService> logger, IServerStateProvider serverState)
    {
        _timer = new Timer(CheckClientProcessHealth, null, Timeout.Infinite, Timeout.Infinite);
        _logger = logger;

        _serverState = serverState;
    }

    public void Start(long? clientProcessId)
    {
        if (!clientProcessId.HasValue)
        {
            _logger.LogWarning("🐛 ClientProcessId was somehow not specified in Initialize request.");
            throw new ArgumentNullException(nameof(clientProcessId));
        }
        if (clientProcessId.Value > int.MaxValue)
        {
            _logger.LogWarning("🐛 ClientProcessId value from Initialize request exceeds System.Int32.MaxValue. What now?");
            throw new ArgumentOutOfRangeException(nameof(clientProcessId));
        }

        // NOTE: we're purposely using a timer to poll process health rather than registering an event handler to watch its exit.
        _process = Process.GetProcessById((int)clientProcessId.Value);

        _didNotify = false;
        _interval = TimeSpan.FromSeconds(_serverState.Options.HealthCheckIntervalSeconds);

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("👀 Starting health check service (Interval: {TotalSeconds} seconds)", _interval.TotalSeconds);
        }
        _timer.Change(TimeSpan.Zero, _interval);
    }

    public void Pause()
    {
        if (!_didNotify)
        {
            _logger.LogInformation("⏯️ Pausing health check service...");
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }
        else
        {
            _logger.LogWarning("🐛 HealthCheckService was already paused.");
            throw new InvalidOperationException();
        }
    }

    public void Resume()
    {
        if (!_didNotify)
        {
            _logger.LogInformation("⏯️ Resuming health check service...");
            _timer.Change(TimeSpan.Zero, _interval);
        }
        else
        {
            _logger.LogWarning("🐛 HealthCheckService was not paused.");
            throw new InvalidOperationException();
        }
    }

    public void Dispose()
    {
        if (_timer is IDisposable disposableTimer)
        {
            disposableTimer.Dispose();
        }
        if (_process is IDisposable disposableProcess)
        {
            disposableProcess.Dispose();
        }
    }

    private void CheckClientProcessHealth(object? state)
    {
        if (_process!.HasExited)
        {
            _logger.LogWarning("⚠️ Client process (ID:{ProcessId}) has exited", _process!.Id);
            OnUnhealthyClientDetected();
        }
    }

    private void OnUnhealthyClientDetected()
    {
        if (!_didNotify)
        {
            _logger.LogCritical("💀 Client process health check failed; server process will be terminated.");

            _timer.Change(TimeSpan.Zero, Timeout.InfiniteTimeSpan);
            _didNotify = true;

            _serverState.OnExit();
        }
    }
}