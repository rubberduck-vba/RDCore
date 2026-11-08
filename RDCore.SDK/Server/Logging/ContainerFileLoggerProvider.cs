using Microsoft.Extensions.Logging;

namespace RDCore.SDK.Server.Logging;

/// <summary>
/// Writes the records of the <c>OmniSharp</c> language server's own container - OmniSharp's input and output handlers, its request scheduler, and the handlers
/// registered there - to the component's file log, through the application's logger.
/// </summary>
/// <remarks>
/// <para>
/// The container has a logging of its own, which writes to the client (<see cref="ScrubbingLanguageServerLoggerProvider"/>) and, without this, nowhere else. A
/// failure of the connection itself - an unhandled exception in the input loop, a write that ends the output loop - is then told only over the connection that
/// failed, and the file has nothing of it.
/// </para>
/// <para>
/// A record is logged as <see cref="ContainerRecord"/>, which <see cref="ClientTraceLoggerProvider"/> does not send: the container's own provider has told the client
/// already, and a warning sent again would be sent once more for each warning sending it raises.
/// </para>
/// </remarks>
/// <param name="appLogger">The application's logger, which writes to its file.</param>
public sealed class ContainerFileLoggerProvider(ILogger appLogger) : ILoggerProvider
{
    /// <summary>
    /// The event of a record of the container, written with the application's logger.
    /// </summary>
    public static EventId ContainerRecord { get; } = new(7_100, "LanguageServerContainer");

    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName) => new ContainerFileLogger(appLogger, categoryName);

    /// <inheritdoc/>
    public void Dispose()
    {
    }
}

/// <summary>
/// The <see cref="ILogger"/> half of <see cref="ContainerFileLoggerProvider"/>.
/// </summary>
internal sealed class ContainerFileLogger(ILogger appLogger, string categoryName) : ILogger
{
    /// <inheritdoc/>
    // never null - see NullLogScope: OmniSharp's request-timing logger disposes this without a null check.
    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullLogScope.Instance;

    /// <inheritdoc/>
    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && appLogger.IsEnabled(logLevel);

    /// <inheritdoc/>
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        appLogger.Log(logLevel, ContainerFileLoggerProvider.ContainerRecord, exception, "{Category}: {Message}", categoryName, formatter(state, exception));
    }
}
