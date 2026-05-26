using RDCore.CLI.App.Messages.Model;
using RDCore.SDK.Model.Errors;
using System.Collections.Immutable;

namespace RDCore.CLI.App.Messages;

/// <summary>
/// Builds the <c>ConsoleMessagePart</c> components of a console message.
/// </summary>
internal record class ConsoleMessageBuilder
{
    public MessageKind Kind { get; init; } = MessageKind.Trace;
    public ImmutableArray<ConsoleMessagePart> Parts { get; init; } = [];

    public ConsoleMessageBuilder WithKind(MessageKind kind) => this with { Kind = kind };
    public ConsoleMessageBuilder WithTimestamp(DateTimeOffset timestamp) => WithUniquePart(ConsoleMessageTimestampPartFactory.CreateTimestampPart(timestamp));
    public ConsoleMessageBuilder WithTitle(string title) => WithUniquePart(ConsoleMessageTitlePartFactory.CreateTitlePart(title));
    public ConsoleMessageBuilder WithTitle(VBCompileErrorException exception) => WithUniquePart(ConsoleMessageTitlePartFactory.CreateTitlePart(exception));
    public ConsoleMessageBuilder WithTitle(VBRuntimeErrorException exception) => WithUniquePart(ConsoleMessageTitlePartFactory.CreateTitlePart(exception));
    public ConsoleMessageBuilder WithTitle(VBApplicationErrorException exception) => WithUniquePart(ConsoleMessageTitlePartFactory.CreateTitlePart(exception));
    public ConsoleMessageBuilder WithTitle(Exception exception) => WithUniquePart(ConsoleMessageTitlePartFactory.CreateTitlePart(exception));
    public ConsoleMessageBuilder WithMessageBody(string body, string? color = default) => WithUniquePart(ConsoleMessageBodyPartFactory.CreateMessageBodyPart(body, color));
    //public ConsoleMessageBuilder WithMessageOverlay(string overlay, string color, int lineStart, int lines) => WithPart(ConsoleMessageOverlayMessagePartFactory.CreateMessageOverlayBodyPart(overlay, color, lineStart, lines));
    public ConsoleMessageBuilder WithMessageBody(Exception exception) => WithUniquePart(ConsoleMessageBodyPartFactory.CreateMessageBodyPart(exception));
    public ConsoleMessageBuilder WithVerbose(string verbose) => WithUniquePart(ConsoleMessageVerbosePartFactory.CreateVerbosePart(verbose));
    public ConsoleMessageBuilder WithStackTrace(Exception exception) => WithUniquePart(ConsoleMessageStackTracePartFactory.CreateStackTracePart(exception));
    public ConsoleMessageBuilder WithMetric(MetricKind kind, string placeholder, double value) => WithPart(ConsoleMessageMetricPartFactory.CreateMetricPart(kind, placeholder, value));
    public ConsoleMessageBuilder WithMetric(MetricKind kind, string placeholder, string value) => WithPart(ConsoleMessageMetricPartFactory.CreateMetricPart(placeholder, value));
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
