using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.Services.States;
using System.Diagnostics;

namespace RDCore.SDK.Server.Services
{
    public interface IHealthCheckService<out TApp> : IDisposable
        where TApp : IRDCoreApp
    {
        void Start(int processId, Action onUnhealthyProcess);
        void Pause();
        void Resume();
    }

    public sealed class HealthCheckService<TApp> : IHealthCheckService<TApp>
        where TApp : IRDCoreApp
    {
        private readonly Timer _timer;
        private readonly ILogger _logger;

        private readonly IServerStateProvider _serverState;

        private TimeSpan _interval;

        private bool _didNotify;
        private Process? _process;
        private Action? _handleUnhealthy;

        private readonly IOptions<SdkServerOptions> _options; // TODO handle configuration changes

        public HealthCheckService(ILogger<HealthCheckService<TApp>> logger, 
            IServerStateProvider serverState, 
            IOptions<SdkServerOptions> options)
        {
            TimerCallback callback = typeof(TApp) is IRDCoreServerApp 
                ? CheckClientProcessHealth 
                : CheckServerProcessHealth;

            _timer = new Timer(callback, null, Timeout.Infinite, Timeout.Infinite);
            _logger = logger;

            _options = options;
            _serverState = serverState;
        }

        public void Start(int processId, Action onUnhealthyProcess)
        {
            // NOTE: we're purposely using a timer to poll process health rather than registering an event handler to watch its exit.
            _process = Process.GetProcessById(processId);
            _handleUnhealthy = onUnhealthyProcess;

            _didNotify = false;
            _interval = TimeSpan.FromSeconds(_options.Value.HealthCheckIntervalSeconds);

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Starting health check service (Interval: {TotalSeconds} seconds)", _interval.TotalSeconds);
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
                _logger.LogWarning("HealthCheckService was already paused.");
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
                _logger.LogWarning("HealthCheckService was not paused.");
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
                _logger.LogWarning("Client process (ID:{ProcessId}) has exited", _process!.Id);
                OnUnhealthyClientDetected();
            }
        }

        private void CheckServerProcessHealth(object? state)
        {
            if (_process!.HasExited)
            {
                _logger.LogWarning("Server process (ID:{ProcessId}) has exited", _process!.Id);
                OnUnhealthyServerDetected();
            }
        }

        private void OnUnhealthyClientDetected()
        {
            if (!_didNotify)
            {
                _logger.LogCritical("Client process health check failed; server process will be terminated.");
                _timer.Change(TimeSpan.Zero, Timeout.InfiniteTimeSpan);

                _handleUnhealthy?.Invoke();
                _didNotify = true;
            }
        }

        private void OnUnhealthyServerDetected()
        {
            if (!_didNotify)
            {
                _logger.LogCritical("Server process health check failed; restarting process...");
                _timer.Change(TimeSpan.Zero, Timeout.InfiniteTimeSpan);

                _handleUnhealthy?.Invoke();
                _didNotify = true;
            }
        }
    }
}