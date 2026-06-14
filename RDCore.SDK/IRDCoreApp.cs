using Microsoft.Extensions.Logging;
namespace RDCore.SDK;

/// <summary>
/// A common interface encompassing all RDCore applications.
/// </summary>
public interface IRDCoreApp : IDisposable
{
    /// <summary>
    /// Bootstraps and starts the application.
    /// </summary>
    /// <param name="provider">An <see cref="IServiceProvider"/> to configure the application.</param>
    Task RunAsync(IServiceProvider provider);
    /// <summary>
    /// Logs the specified message at the specified level, if logging is enabled at that level.
    /// </summary>
    /// <param name="logLevel">The <see cref="LogLevel"/> for this message.</param>
    /// <param name="message">The log <c>message</c>.</param>
    void LogIfEnabled(LogLevel logLevel, string message);
}
