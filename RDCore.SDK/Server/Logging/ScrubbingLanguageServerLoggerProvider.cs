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
/// Forwards a server component's internal log records to its LSP client as <c>window/logMessage</c>
/// notifications — the same behaviour as OmniSharp's <c>AddLanguageProtocolLogging()</c> — but runs
/// every forwarded message through <see cref="SourcePathAnonymizer"/> first.
/// </summary>
/// <remarks>
/// OmniSharp's request pipeline logs an unhandled handler fault with <c>LogCritical(exception, …)</c>,
/// and its protocol logger appends <see cref="System.Exception.ToString"/> to the message verbatim.
/// On a Debug/PDB build (the dev platform, and any publish that ships portable PDBs) those frames
/// carry the build machine's absolute source paths, so the raw stack would reach the client over the
/// wire. Scrubbing at this single forwarding point closes that channel for every category and level.
/// The unredacted record is still written to the component's own file log.
/// </remarks>
/// <param name="languageServer">The facade whose <c>window/logMessage</c> channel carries the forwarded records.</param>
/// <param name="serverOptions">
/// Supplies <see cref="SdkServerOptions.WireErrorDetail"/> — how a source path in a forwarded record
/// is rewritten.
/// </param>
public sealed class ScrubbingLanguageServerLoggerProvider(ILanguageServerFacade languageServer, IOptions<SdkServerOptions> serverOptions) : ILoggerProvider
{
    private readonly SourcePathScrubMode _scrubMode = serverOptions.Value.WireErrorDetail;

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new ScrubbingLanguageServerLogger(languageServer, categoryName, _scrubMode);

    /// <inheritdoc />
    public void Dispose()
    {
    }
}

/// <summary>
/// The <see cref="ILogger"/> half of <see cref="ScrubbingLanguageServerLoggerProvider"/>. Mirrors the
/// message composition of OmniSharp's internal <c>LanguageServerLogger</c> so the forwarded text is
/// unchanged apart from the scrub pass.
/// </summary>
internal sealed class ScrubbingLanguageServerLogger(ILanguageServerFacade responseRouter, string categoryName, SourcePathScrubMode scrubMode) : ILogger
{
    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc />
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        // the scoped ILanguageServerFacade resolves to the server instance, which is not assigned until
        // it finishes constructing; a record logged before then is kept in the file log but has no
        // wire to travel yet.
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
    /// Builds the record text exactly the way OmniSharp's <c>LanguageServerLogger</c> does:
    /// <c>category: message[ - exception] | key='value' …</c>. Kept as a pure method so the scrub
    /// contract can be unit-tested without a language-server facade.
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

    /// <summary>Maps a <see cref="LogLevel"/> to an LSP <see cref="MessageType"/>; <c>None</c> is not forwarded.</summary>
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
