using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RDCore.CLI.App.Messages.Model;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Errors.Abstract;
using RDCore.SDK.Server.Configuration;
using System.Collections.Immutable;

namespace RDCore.CLI.App.Messages;

public interface IRDCoreLogger : ILogger
{
    void Log(LogLevel level, string title, string message, string verbose);
    void Log(VBSyntaxErrorInfo error);
    void Log(VBCompileErrorInfo error);
    void Log(VBRuntimeErrorInfo error);
    void Log(VBApplicationErrorInfo error);
    void Log(Exception exception);
}

public sealed class RDCoreConsoleLoggerProvider(IOptions<SdkServerOptions> Options, IConsoleMessageWriter Writer) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new RDCoreConsoleLogger(Options, Writer);

    public void Dispose() 
    {
    }
}

public class RDCoreConsoleLogger(IOptions<SdkServerOptions> Options, IConsoleMessageWriter Writer) : IRDCoreLogger
{
    private readonly ConsoleMessageBuilder _builder = new();
    private readonly IConsoleMessageWriter _writer = Writer;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        throw new NotSupportedException();
    }

    public bool IsEnabled(LogLevel logLevel) => logLevel <= Options.Value.TraceLevel;

    public void Log(ConsoleMessageBuilder builder)
    {
        _writer.WriteMessage(builder);
    }

    public void Log(LogLevel level, string title, string message, string verbose)
    {
        if (IsEnabled(level))
        {
            var builder = level switch
            {
                LogLevel.Trace => _builder.WithKind(MessageKind.Trace),
                LogLevel.Debug => _builder.WithKind(MessageKind.Trace),
                LogLevel.Information => _builder.WithKind(MessageKind.Information),
                LogLevel.Warning => _builder.WithKind(MessageKind.Warning),
                LogLevel.Error => _builder.WithKind(MessageKind.Error),
                LogLevel.Critical => _builder.WithKind(MessageKind.Error),
                _ => _builder
            };
            builder.WithTimestamp(DateTimeOffset.UtcNow);
            builder.WithTitle(title);
            builder.WithMessageBody(message);
            if (Options.Value.Verbose)
            {
                builder.WithVerbose(verbose);
            }

            _writer.WriteMessage(builder);
        }
    }

    private void Log(VBErrorInfo error, string code)
    {
        if (IsEnabled(LogLevel.Error))
        {
            var builder = _builder
                .WithKind(MessageKind.Error)
                .WithTimestamp(DateTimeOffset.UtcNow)
                .WithTitle(code)
                .WithMessageBody(error.Description);
            if (Options.Value.Verbose)
            {
                builder.WithVerbose(error.Verbose);
            }

            _writer.WriteMessage(builder);
        }
    }

    public void Log(VBSyntaxErrorInfo error) => Log(error, error.ToDiagnosticCode());
    public void Log(VBCompileErrorInfo error) => Log(error, error.ToDiagnosticCode());
    public void Log(VBRuntimeErrorInfo error) => Log(error, error.ToDiagnosticCode());
    public void Log(VBApplicationErrorInfo error) => Log(error, error.ToDiagnosticCode());

    public void Log(Exception exception)
    {
        if (IsEnabled(LogLevel.Critical))
        {
            var builder = _builder
                .WithKind(MessageKind.Error)
                .WithTimestamp(DateTimeOffset.UtcNow)
                .WithTitle(exception.GetType().Name)
                .WithMessageBody(exception.Message);
            if (Options.Value.Verbose)
            {
                builder.WithStackTrace(exception);
            }

            _writer.WriteMessage(builder);
        }
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
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
        }
    }
}

/// <summary>
/// Builds the <c>ConsoleMessagePart</c> components of a console message.
/// </summary>
public record class ConsoleMessageBuilder
{
    public MessageKind Kind { get; init; } = MessageKind.Trace;
    public ImmutableArray<ConsoleMessagePart> Parts { get; init; } = [];
    public bool IsWithLineBreak { get; init; }
    public ConsoleMessageBuilder WithLineBreak(bool withLineBreak = true) => this with { IsWithLineBreak = withLineBreak };

    public ConsoleMessageBuilder WithKind(MessageKind kind) => this with { Kind = kind };
    public ConsoleMessageBuilder WithTimestamp(DateTimeOffset timestamp) => WithUniquePart(ConsoleMessageTimestampPartFactory.CreateTimestampPart(timestamp));
    public ConsoleMessageBuilder WithTitle(string title) => WithUniquePart(ConsoleMessageTitlePartFactory.CreateTitlePart(title));
    public ConsoleMessageBuilder WithTitle(VBSyntaxErrorInfo error) => WithUniquePart(ConsoleMessageTitlePartFactory.CreateTitlePart(error.ToDiagnosticCode()));
    public ConsoleMessageBuilder WithTitle(VBCompileErrorInfo error) => WithUniquePart(ConsoleMessageTitlePartFactory.CreateTitlePart(error.ToDiagnosticCode()));
    public ConsoleMessageBuilder WithTitle(VBRuntimeErrorInfo error) => WithUniquePart(ConsoleMessageTitlePartFactory.CreateTitlePart(error.ToDiagnosticCode()));
    public ConsoleMessageBuilder WithTitle(VBApplicationErrorInfo error) => WithUniquePart(ConsoleMessageTitlePartFactory.CreateTitlePart(error.ToDiagnosticCode()));
    public ConsoleMessageBuilder WithTitle(Exception exception) => WithUniquePart(ConsoleMessageTitlePartFactory.CreateTitlePart(exception.GetType().Name));
    public ConsoleMessageBuilder WithMessageBody(string body, string? color = default) => WithUniquePart(ConsoleMessageBodyPartFactory.CreateMessageBodyPart(body, color));
    //public ConsoleMessageBuilder WithMessageOverlay(string overlay, string color, int lineStart, int lines) => WithPart(ConsoleMessageOverlayMessagePartFactory.CreateMessageOverlayBodyPart(overlay, color, lineStart, lines));
    public ConsoleMessageBuilder WithMessageBody(Exception exception) => WithUniquePart(ConsoleMessageBodyPartFactory.CreateMessageBodyPart(exception));
    public ConsoleMessageBuilder WithMessageBody(VBErrorInfo error) => WithUniquePart(ConsoleMessageBodyPartFactory.CreateMessageBodyPart(error.Description));
    public ConsoleMessageBuilder WithVerbose(string verbose) => WithUniquePart(ConsoleMessageVerbosePartFactory.CreateVerbosePart(verbose));
    public ConsoleMessageBuilder WithVerbose(VBErrorInfo error) => WithUniquePart(ConsoleMessageVerbosePartFactory.CreateVerbosePart(error.Verbose));
    public ConsoleMessageBuilder WithStackTrace(Exception exception) => WithUniquePart(ConsoleMessageStackTracePartFactory.CreateStackTracePart(exception));
    public ConsoleMessageBuilder WithPlaceholder(PlaceholderKind kind, string placeholder, double value) => WithPart(ConsoleMessageMetricPartFactory.CreatePlaceholderPart(kind, $"{{${placeholder}}}", value));
    public ConsoleMessageBuilder WithPlaceholder(string placeholder, string value) => WithPart(ConsoleMessageMetricPartFactory.CreatePlaceholderPart($"{{${placeholder}}}", value));
    public ConsoleMessageBuilder WithPart(ConsoleMessagePart part) => this with { Parts = [.. Parts, part] };
    private ConsoleMessageBuilder WithUniquePart<TPart>(TPart part) where TPart : ConsoleMessagePart
    {
        if (!Parts.Any(e => e.Part == part.Part))
        {
            return WithPart(part);
        }
        return this;
    }
    /// <summary>
    /// Gets the last <see cref="MessagePart.Title"/> message part written to this builder.
    /// </summary>
    public string Title => Parts.LastOrDefault(part => part.Part == MessagePart.Title)?.Value ?? string.Empty;
    /// <summary>
    /// Gets the last <see cref="MessagePart.Body"/> message part written to this builder.
    /// </summary>
    public string Body => Parts.LastOrDefault(part => part.Part == MessagePart.Body)?.Value ?? string.Empty;
    /// <summary>
    /// Gets the last <see cref="MessagePart.Verbose"/> message part written to this builder.
    /// </summary>
    public string Verbose => Parts.LastOrDefault(part => part.Part == MessagePart.Verbose)?.Value ?? string.Empty;
    /// <summary>
    /// Gets the last <see cref="MessagePart.StackTrace"/> message part written to this builder.
    /// </summary>
    public string StackTrace => Parts.LastOrDefault(part => part.Part == MessagePart.StackTrace)?.Value ?? string.Empty;
}
