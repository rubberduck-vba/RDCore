using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RDCore.SDK.ConsoleIO.Model;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Errors.Abstract;
using RDCore.SDK.Server.Configuration;

namespace RDCore.SDK.ConsoleIO;

/// <summary>
/// An <see cref="ILogger"/> that renders structured console messages, plus typed overloads for the
/// RD-VBA error kinds and pre-built <see cref="ConsoleMessageBuilder"/>s.
/// </summary>
public interface IRDCoreLogger : ILogger
{
    /// <summary>Renders a message at <paramref name="level"/> with an explicit title and verbose text.</summary>
    void Log(LogLevel level, string title, string message, string verbose);

    /// <summary>Renders a syntax error.</summary>
    void Log(VBSyntaxErrorInfo error);

    /// <summary>Renders a compile error.</summary>
    void Log(VBCompileErrorInfo error);

    /// <summary>Renders a runtime error.</summary>
    void Log(VBRuntimeErrorInfo error);

    /// <summary>Renders an application error.</summary>
    void Log(VBApplicationErrorInfo error);

    /// <summary>Renders an exception.</summary>
    void Log(Exception exception);
}

/// <summary>
/// An <see cref="ILoggerProvider"/> whose loggers render through an <see cref="IConsoleMessageWriter"/>.
/// Add it to a host's logging pipeline to route <see cref="ILogger{TCategoryName}"/> output to the
/// console renderer.
/// </summary>
/// <param name="options">Server options — supplies the trace-level floor and the verbose flag.</param>
/// <param name="writer">The renderer messages are written to.</param>
public sealed class RDCoreConsoleLoggerProvider(IOptions<SdkServerOptions> options, IConsoleMessageWriter writer) : ILoggerProvider
{
    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName) => new RDCoreConsoleLogger(options, writer);

    /// <inheritdoc/>
    public void Dispose()
    {
    }
}

/// <summary>
/// Renders <see cref="ILogger"/> calls as structured console messages through an
/// <see cref="IConsoleMessageWriter"/>.
/// </summary>
/// <param name="options">Server options — supplies the trace-level floor and the verbose flag.</param>
/// <param name="writer">The renderer messages are written to.</param>
public class RDCoreConsoleLogger(IOptions<SdkServerOptions> options, IConsoleMessageWriter writer) : IRDCoreLogger
{
    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }

    /// <inheritdoc/>
    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    /// <summary>
    /// Enabled when <paramref name="logLevel"/> is at least as severe as the configured
    /// <see cref="SdkServerOptions.TraceLevel"/> floor (and neither is <see cref="LogLevel.None"/>).
    /// </summary>
    public bool IsEnabled(LogLevel logLevel)
        => logLevel != LogLevel.None
           && options.Value.TraceLevel != LogLevel.None
           && logLevel >= options.Value.TraceLevel;

    /// <summary>Renders a pre-built message.</summary>
    public void Log(ConsoleMessageBuilder builder) => writer.WriteMessage(builder);

    /// <inheritdoc/>
    public void Log(LogLevel level, string title, string message, string verbose)
    {
        if (!IsEnabled(level))
        {
            return;
        }

        var kind = level switch
        {
            LogLevel.Trace or LogLevel.Debug => MessageKind.Trace,
            LogLevel.Information => MessageKind.Information,
            LogLevel.Warning => MessageKind.Warning,
            LogLevel.Error or LogLevel.Critical => MessageKind.Error,
            _ => MessageKind.Trace,
        };

        // ConsoleMessageBuilder is immutable: each With* returns a new instance, so the chain is
        // reassigned. No timestamp — a console line is not a log-file line; a caller that wants one
        // adds it explicitly, and a future file renderer of the same builder can add its own.
        var builder = new ConsoleMessageBuilder()
            .WithKind(kind)
            .WithTitle(title)
            .WithMessageBody(message);
        if (options.Value.Verbose && verbose.Length > 0)
        {
            builder = builder.WithVerbose(verbose);
        }

        writer.WriteMessage(builder);
    }

    private void Log(VBErrorInfo error, string code)
    {
        if (!IsEnabled(LogLevel.Error))
        {
            return;
        }

        var builder = new ConsoleMessageBuilder()
            .WithKind(MessageKind.Error)
            .WithTitle(code)
            .WithMessageBody(error.Description);
        if (options.Value.Verbose)
        {
            builder = builder.WithVerbose(error.Verbose);
        }

        writer.WriteMessage(builder);
    }

    /// <inheritdoc/>
    public void Log(VBSyntaxErrorInfo error) => Log(error, error.ToDiagnosticCode());

    /// <inheritdoc/>
    public void Log(VBCompileErrorInfo error) => Log(error, error.ToDiagnosticCode());

    /// <inheritdoc/>
    public void Log(VBRuntimeErrorInfo error) => Log(error, error.ToDiagnosticCode());

    /// <inheritdoc/>
    public void Log(VBApplicationErrorInfo error) => Log(error, error.ToDiagnosticCode());

    /// <inheritdoc/>
    public void Log(Exception exception)
    {
        if (!IsEnabled(LogLevel.Critical))
        {
            return;
        }

        var builder = new ConsoleMessageBuilder()
            .WithKind(MessageKind.Error)
            .WithTitle(exception.GetType().Name)
            .WithMessageBody(exception.Message);
        if (options.Value.Verbose)
        {
            builder = builder.WithStackTrace(exception);
        }

        writer.WriteMessage(builder);
    }

    /// <inheritdoc/>
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        if (exception is not null)
        {
            Log(exception);
            return;
        }

        switch (state)
        {
            case VBSyntaxErrorInfo syntaxError:
                Log(syntaxError);
                break;
            case VBCompileErrorInfo compileError:
                Log(compileError);
                break;
            case VBRuntimeErrorInfo runtimeError:
                Log(runtimeError);
                break;
            case VBApplicationErrorInfo appError:
                Log(appError);
                break;
            case string message:
                Log(logLevel, string.Empty, message, string.Empty);
                break;
            default:
                // structured logging: honour the message-template formatter for any other state.
                if (formatter is not null)
                {
                    Log(logLevel, string.Empty, formatter(state, exception), string.Empty);
                }
                break;
        }
    }
}
