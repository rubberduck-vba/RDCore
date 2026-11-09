using RDCore.CLI.ConsoleIO.Model;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Errors.Abstract;
using System.Collections.Immutable;

namespace RDCore.CLI.ConsoleIO;

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

    public ConsoleMessageBuilder WithMessageBody(string body) => WithUniquePart(ConsoleMessageBodyPartFactory.CreateMessageBodyPart(body));

    public ConsoleMessageBuilder WithMessageBody(Exception exception) => WithUniquePart(ConsoleMessageBodyPartFactory.CreateMessageBodyPart(exception));

    public ConsoleMessageBuilder WithMessageBody(VBErrorInfo error) => WithUniquePart(ConsoleMessageBodyPartFactory.CreateMessageBodyPart(error.Description));

    public ConsoleMessageBuilder WithVerbose(string verbose) => WithUniquePart(ConsoleMessageVerbosePartFactory.CreateVerbosePart(verbose));

    public ConsoleMessageBuilder WithVerbose(VBErrorInfo error) => WithUniquePart(ConsoleMessageVerbosePartFactory.CreateVerbosePart(error.Verbose));

    public ConsoleMessageBuilder WithStackTrace(Exception exception) => WithUniquePart(ConsoleMessageStackTracePartFactory.CreateStackTracePart(exception));

    public ConsoleMessageBuilder WithPlaceholder(PlaceholderKind kind, string placeholder, double value) => WithPart(ConsoleMessageMetricPartFactory.CreatePlaceholderPart(kind, $"{{${placeholder}}}", value));

    public ConsoleMessageBuilder WithPlaceholder(string placeholder, string value) => WithPart(ConsoleMessageMetricPartFactory.CreatePlaceholderPart($"{{${placeholder}}}", value));

    public ConsoleMessageBuilder WithPlaceholder(string placeholder, string value, ConsoleRgbColor color)
        => WithPart(ConsoleMessageMetricPartFactory.CreatePlaceholderPart($"{{${placeholder}}}", value, color));

    public ConsoleMessageBuilder WithPart(ConsoleMessagePart part) => this with { Parts = [.. Parts, part] };

    private ConsoleMessageBuilder WithUniquePart<TPart>(TPart part) where TPart : ConsoleMessagePart
        => Parts.Any(existing => existing.Part == part.Part) ? this : WithPart(part);

    public string Title => Parts.LastOrDefault(part => part.Part == MessagePart.Title)?.Value ?? string.Empty;

    public string Body => Parts.LastOrDefault(part => part.Part == MessagePart.Body)?.Value ?? string.Empty;

    public string Verbose => Parts.LastOrDefault(part => part.Part == MessagePart.Verbose)?.Value ?? string.Empty;

    public string StackTrace => Parts.LastOrDefault(part => part.Part == MessagePart.StackTrace)?.Value ?? string.Empty;
}
