using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace RDCore.SDK.Server.Logging;

/// <summary>
/// A no-op scope for an <see cref="ILogger"/> that keeps no scope state.
/// </summary>
/// <remarks>
/// <see cref="ILogger.BeginScope{TState}"/> must never return <c>null</c>: OmniSharp's
/// <c>TimeLoggerExtensions</c> wraps request routing (including <c>initialize</c>) in
/// <c>logger.BeginScope(…)</c> and disposes the result with no null check, so a <c>null</c> scope
/// throws a <see cref="NullReferenceException"/> out of the <c>initialize</c> route — the server
/// then never fires <c>OnStarted</c> and the client hangs mid-handshake.
/// </remarks>
internal sealed class NullLogScope : IDisposable
{
    internal static readonly NullLogScope Instance = new();

    public void Dispose()
    {
    }
}

/// <summary>
/// Shared shaping for the two paths that forward <see cref="ILogger"/> records to an LSP client —
/// the inner-container <see cref="ScrubbingLanguageServerLoggerProvider"/> and the outer-host
/// <see cref="ClientTraceLoggerProvider"/> — so the message layout and level mapping cannot drift.
/// </summary>
internal static class LspProtocolLog
{
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

    /// <summary>
    /// Decides, for a record at <paramref name="level"/> and the client's current <c>$/setTrace</c>
    /// level <paramref name="trace"/>, which client channels carry it: <c>window/logMessage</c> for a
    /// warning or worse (always, so problems surface even with trace off), <c>$/logTrace</c> for
    /// narration when trace is on, and whether that trace record includes its verbose detail.
    /// </summary>
    internal static (bool LogMessage, bool LogTrace, bool IncludeVerbose) Route(LogLevel level, InitializeTrace trace)
        => (
            LogMessage: level is not LogLevel.None && level >= LogLevel.Warning,
            LogTrace: level is not LogLevel.None && level < LogLevel.Warning && trace != InitializeTrace.Off,
            IncludeVerbose: trace == InitializeTrace.Verbose);

    /// <summary>
    /// Maps a <see cref="LogLevel"/> to the LSP <see cref="MessageType"/> for a
    /// <c>window/logMessage</c>. <see cref="LogLevel.None"/> maps to nothing and returns <c>false</c>.
    /// </summary>
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
