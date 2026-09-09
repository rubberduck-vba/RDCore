using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Window;
using RDCore.SDK.ConsoleIO;
using RDCore.SDK.Server.Configuration;

namespace RDCore.SDK.Server.Logging;

/// <summary>
/// A drop-in replacement for OmniSharp's <c>AddLanguageProtocolLogging()</c>: forwards a server
/// component's internal log records to its LSP client as <c>window/logMessage</c> notifications, but
/// scrubs every forwarded message through <see cref="SourcePathAnonymizer"/> first so a caught
/// exception's stack cannot carry a build-machine source path off the machine. The unredacted record
/// still reaches the component's own file log.
/// </summary>
/// <param name="languageServer">The facade whose <c>window/logMessage</c> channel carries the forwarded records.</param>
/// <param name="serverOptions">Supplies <see cref="SdkServerOptions.WireErrorDetail"/> — how a forwarded source path is rewritten.</param>
public sealed class ScrubbingLanguageServerLoggerProvider(ILanguageServerFacade languageServer, IOptions<SdkServerOptions> serverOptions) : ILoggerProvider
{
    private readonly SourcePathScrubMode _scrubMode = serverOptions.Value.WireErrorDetail;

    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName) => new ScrubbingLanguageServerLogger(languageServer, categoryName, _scrubMode);

    /// <inheritdoc/>
    public void Dispose()
    {
    }
}

/// <summary>
/// The <see cref="ILogger"/> half of <see cref="ScrubbingLanguageServerLoggerProvider"/>. Composes
/// the record text the same way OmniSharp's internal <c>LanguageServerLogger</c> does, then scrubs it.
/// </summary>
internal sealed class ScrubbingLanguageServerLogger(ILanguageServerFacade responseRouter, string categoryName, SourcePathScrubMode scrubMode) : ILogger
{
    /// <inheritdoc/>
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    /// <inheritdoc/>
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc/>
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        // the scoped facade is the server instance, which is null until it finishes constructing;
        // a record logged before then stays in the file log but has no wire yet.
        if (responseRouter is null || !TryGetMessageType(logLevel, out var messageType))
        {
            return;
        }

        responseRouter.Window.Log(new LogMessageParams
        {
            Type = messageType,
            Message = SourcePathAnonymizer.Scrub(ComposeMessage(categoryName, state, exception, formatter), scrubMode),
        });
    }

    /// <summary>
    /// The record text in OmniSharp's <c>LanguageServerLogger</c> layout:
    /// <c>category: message[ - exception] | key='value' …</c>.
    /// </summary>
    internal static string ComposeMessage<TState>(string category, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var pairs = state is IEnumerable<KeyValuePair<string, object>> structured
            ? string.Join(" ", structured.Where(pair => pair.Key != "{OriginalFormat}").Select(pair => $"{pair.Key}='{pair.Value}'"))
            : JsonConvert.SerializeObject(state).Replace("\"", "'");

        return category + ": " + formatter(state, exception)
            + (exception is not null ? " - " + exception : string.Empty)
            + " | " + pairs;
    }

    // LogLevel -> LSP MessageType; None is not forwarded.
    internal static bool TryGetMessageType(LogLevel logLevel, out MessageType messageType)
    {
        switch (logLevel)
        {
            case LogLevel.Critical:
            case LogLevel.Error:
                messageType = MessageType.Error;
                return true;
            case LogLevel.Warning:
                messageType = MessageType.Warning;
                return true;
            case LogLevel.Information:
                messageType = MessageType.Info;
                return true;
            case LogLevel.Debug:
            case LogLevel.Trace:
                messageType = MessageType.Log;
                return true;
            default:
                messageType = MessageType.Log;
                return false;
        }
    }
}
