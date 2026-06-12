using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RDCore.SDK.Extensibility.Configuration;

namespace RDCore.SDK.Extensibility;

/// <summary>
/// A common interface that encompasses all <c>RDCore</c> client and server (+extensions) applications.
/// </summary>
public interface IRDCoreApp : IDisposable
{
    /// <summary>
    /// Starts the application.
    /// </summary>
    /// <param name="provider">A <see cref="IServiceProvider"/> supplied by the host.</param>
    /// <param name="token">A process-level <see cref="CancellationToken"/>.</param>
    /// <remarks>
    /// <c>await</c> the returned task to block execution until the host exits.
    /// </remarks>
    Task RunAsync(IServiceProvider provider, CancellationToken token);
    /// <summary>
    /// Logs the specified <c>message</c> at the specified <see cref="LogLevel"/>, if logging is enabled at that level.
    /// </summary>
    /// <param name="logLevel">The level of this log message.</param>
    /// <param name="message">The message to be logged.</param>
    void LogIfEnabled(LogLevel logLevel, string message);
}

/// <summary>
/// The base class for all <c>RDCore</c> client and server (+extensions) applications.
/// </summary>
/// <param name="options">The current application configuration.</param>
/// <param name="logger">A standard logger.</param>
public abstract class RDCoreApp(IOptions<SdkServerOptions> options, ILogger<RDCoreApp> logger) : IRDCoreApp
{
    private readonly IOptions<SdkServerOptions> _options = options;
    private readonly ILogger<RDCoreApp> _logger = logger;
    /// <summary>
    /// The <see cref="IServiceProvider"/> configured at startup to bootstrap the application.
    /// </summary>
    protected IServiceProvider BootstrapProvider { get; set; } = default!;
    /// <summary>
    /// Gets the current configuration settings.
    /// </summary>
    protected SdkServerOptions Options => _options.Value;
    /// <summary>
    /// Logs the specified <c>message</c> at the specified <see cref="LogLevel"/>, if logging is enabled at that level.
    /// </summary>
    /// <param name="logLevel">The level of this log message.</param>
    /// <param name="message">The message to be logged.</param>
    public void LogIfEnabled(LogLevel logLevel, string message)
    {
        if (_logger.IsEnabled(logLevel))
        {
            _logger.Log(logLevel, "{message}", message);
        }
    }

    /// <summary>
    /// Starts the application.
    /// </summary>
    /// <param name="provider">A <see cref="IServiceProvider"/> supplied by the host.</param>
    /// <param name="token">A process-level <see cref="CancellationToken"/>.</param>
    /// <remarks>
    /// <c>await</c> the returned task to block execution until the host exits.
    /// </remarks>
    public async Task RunAsync(IServiceProvider provider, CancellationToken token)
    {
        LogIfEnabled(LogLevel.Information, TraceMessages.LanguageClientStarting);
        BootstrapProvider = provider;
        await StartAppAsync(token);
    }

    /// <summary>
    /// Starts the application using the <see cref="BootstrapProvider"/>.
    /// </summary>
    protected abstract Task StartAppAsync(CancellationToken token);
    /// <summary>
    /// Implement to dispose of any unmanaged resources.
    /// </summary>
    public abstract void Dispose();
}
