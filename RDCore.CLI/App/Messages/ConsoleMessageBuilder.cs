using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RDCore.CLI.App.Messages.Model;
using RDCore.SDK.Model.Errors;
using System.Collections.Immutable;

namespace RDCore.CLI.App.Messages;

public sealed class RDCoreConsoleLoggerProvider(IOptions<RDCoreConsoleLogger.RDCoreConsoleLoggerOptions> Options, IConsoleMessageWriter Writer) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new RDCoreConsoleLogger(Options, Writer);

    public void Dispose() 
    {
    }
}

public class RDCoreConsoleLogger(IOptions<RDCoreConsoleLogger.RDCoreConsoleLoggerOptions> Options, IConsoleMessageWriter Writer) : ILogger
{
    public record class RDCoreConsoleLoggerOptions
    {
        public LogLevel MinLevel { get; init; }
    }

    private static ConsoleMessageBuilder MessageBuilder { get; } = new();

    private readonly IConsoleMessageWriter _writer = Writer;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        throw new NotSupportedException();
    }

    public bool IsEnabled(LogLevel logLevel) => logLevel <= Options.Value.MinLevel;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var builder = logLevel switch
        {
            LogLevel.Trace => RDCoreConsoleLogger.MessageBuilder.WithKind(MessageKind.Trace),
            LogLevel.Debug => RDCoreConsoleLogger.MessageBuilder.WithKind(MessageKind.Trace),
            LogLevel.Information => RDCoreConsoleLogger.MessageBuilder.WithKind(MessageKind.Information),
            LogLevel.Warning => RDCoreConsoleLogger.MessageBuilder.WithKind(MessageKind.Warning),
            LogLevel.Error => RDCoreConsoleLogger.MessageBuilder.WithKind(MessageKind.Error),
            LogLevel.Critical => RDCoreConsoleLogger.MessageBuilder.WithKind(MessageKind.Error),
            _ => RDCoreConsoleLogger.MessageBuilder
        };
        
        _writer.WriteMessage(builder.WithMessageBody(formatter(state, exception)));
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
    public ConsoleMessageBuilder WithTitle(Exception exception) => WithUniquePart(ConsoleMessageTitlePartFactory.CreateTitlePart(exception.GetType().Name));
    public ConsoleMessageBuilder WithMessageBody(string body, string? color = default) => WithUniquePart(ConsoleMessageBodyPartFactory.CreateMessageBodyPart(body, color));
    //public ConsoleMessageBuilder WithMessageOverlay(string overlay, string color, int lineStart, int lines) => WithPart(ConsoleMessageOverlayMessagePartFactory.CreateMessageOverlayBodyPart(overlay, color, lineStart, lines));
    public ConsoleMessageBuilder WithMessageBody(Exception exception) => WithUniquePart(ConsoleMessageBodyPartFactory.CreateMessageBodyPart(exception));
    public ConsoleMessageBuilder WithVerbose(string verbose) => WithUniquePart(ConsoleMessageVerbosePartFactory.CreateVerbosePart(verbose));
    public ConsoleMessageBuilder WithStackTrace(Exception exception) => WithUniquePart(ConsoleMessageStackTracePartFactory.CreateStackTracePart(exception));
    public ConsoleMessageBuilder WithPlaceholder(PlaceholderKind kind, string placeholder, double value) => WithPart(ConsoleMessageMetricPartFactory.CreatePlaceholderPart(kind, placeholder, value));
    public ConsoleMessageBuilder WithPlaceholder(string placeholder, string value) => WithPart(ConsoleMessageMetricPartFactory.CreatePlaceholderPart(placeholder, value));
    public ConsoleMessageBuilder WithPart(ConsoleMessagePart part) => this with { Parts = [.. Parts, part] };
    private ConsoleMessageBuilder WithUniquePart<TPart>(TPart part) where TPart : ConsoleMessagePart
    {
        if (!Parts.Any(e => e.Part == part.Part))
        {
            return WithPart(part);
        }
        return this;
    }
}
