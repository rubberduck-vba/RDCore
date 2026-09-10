using Microsoft.Extensions.Logging;

namespace RDCore.SDK.Server.Logging;

/// <summary>
/// Forwards the outer host's <see cref="ILogger"/> records — the language server's bring-up and
/// workspace narration — to the connected LSP client via
/// <see cref="RDCoreServerApp.SendClientTrace"/>. The component's own file-sink copy is unaffected.
/// This runs on the outer host; the OmniSharp-internal container's records are forwarded by
/// <see cref="ScrubbingLanguageServerLoggerProvider"/>.
/// </summary>
/// <param name="app">
/// Resolves the running server app. A factory rather than the instance, because the app singleton
/// may not be constructed yet when the logger factory first materialises this provider.
/// </param>
public sealed class ClientTraceLoggerProvider(Func<RDCoreServerApp> app) : ILoggerProvider
{
    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName) => new ClientTraceLogger(app, categoryName);

    /// <inheritdoc/>
    public void Dispose()
    {
    }
}

/// <summary>
/// The <see cref="ILogger"/> half of <see cref="ClientTraceLoggerProvider"/>.
/// </summary>
internal sealed class ClientTraceLogger(Func<RDCoreServerApp> app, string categoryName) : ILogger
{
    /// <inheritdoc/>
    // never null — see NullLogScope: OmniSharp's request-timing logger disposes this without a null check.
    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullLogScope.Instance;

    /// <inheritdoc/>
    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    /// <inheritdoc/>
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (logLevel == LogLevel.None)
        {
            return;
        }

        var message = LspProtocolLog.ComposeMessage(categoryName, state, exception, formatter);
        app().SendClientTrace(logLevel, message, exception?.ToString());
    }
}
