using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RDCore.CLI.ConsoleIO.Model;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Errors.Abstract;
using RDCore.SDK.Server.Configuration;

namespace RDCore.CLI.ConsoleIO;

public interface IRDCoreLogger : ILogger
{
    void Log(LogLevel level, string title, string message, string verbose);

    void Log(VBSyntaxErrorInfo error);

    void Log(VBCompileErrorInfo error);

    void Log(VBRuntimeErrorInfo error);

    void Log(VBApplicationErrorInfo error);

    void Log(Exception exception);
}

public sealed class RDCoreConsoleLoggerProvider(IOptions<SdkServerOptions> options, IConsoleMessageWriter writer) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new RDCoreConsoleLogger(options, writer);

    public void Dispose()
    {
    }
}

public class RDCoreConsoleLogger(IOptions<SdkServerOptions> options, IConsoleMessageWriter writer) : IRDCoreLogger
{
    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel)
        => logLevel != LogLevel.None
           && options.Value.TraceLevel != LogLevel.None
           && logLevel >= options.Value.TraceLevel;

    public void Log(ConsoleMessageBuilder builder) => writer.WriteMessage(builder);

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

    public void Log(VBSyntaxErrorInfo error) => Log(error, error.ToDiagnosticCode());

    public void Log(VBCompileErrorInfo error) => Log(error, error.ToDiagnosticCode());

    public void Log(VBRuntimeErrorInfo error) => Log(error, error.ToDiagnosticCode());

    public void Log(VBApplicationErrorInfo error) => Log(error, error.ToDiagnosticCode());

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
