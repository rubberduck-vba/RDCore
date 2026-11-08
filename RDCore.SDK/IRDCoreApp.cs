using Microsoft.Extensions.Logging;
using RDCore.SDK.Client;
namespace RDCore.SDK;

/// <summary>
/// A common interface encompassing all RDCore applications.
/// </summary>
public interface IRDCoreApp : IDisposable
{
    /// <summary>
    /// Identifies the type of RDCore platform application.
    /// </summary>
    CoreServerComponent PlatformComponent { get; }
    /// <summary>
    /// Bootstraps and starts the application.
    /// </summary>
    /// <param name="provider">An <see cref="IServiceProvider"/> to configure the application.</param>
    /// <param name="args">The command-line arguments the application was started with.</param>
    Task RunAsync(IServiceProvider provider, string[] args);
    /// <summary>
    /// Logs the specified message at the specified level, if logging is enabled at that level.
    /// </summary>
    /// <param name="logLevel">The <see cref="LogLevel"/> for this message.</param>
    /// <param name="message">The log <c>message</c>.</param>
    void LogIfEnabled(LogLevel logLevel, string message);
}
