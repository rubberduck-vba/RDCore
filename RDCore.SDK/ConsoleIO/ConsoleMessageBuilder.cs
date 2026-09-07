using RDCore.SDK.ConsoleIO.Model;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Errors.Abstract;
using System.Collections.Immutable;

namespace RDCore.SDK.ConsoleIO;

/// <summary>
/// An immutable builder for a structured console message. Each <c>With*</c> call returns a new
/// instance; a renderer consumes <see cref="Parts"/> and <see cref="Kind"/>. At most one part of each
/// <see cref="MessagePart"/> role is kept (last write wins is <em>not</em> applied — the first is kept).
/// </summary>
public record class ConsoleMessageBuilder
{
    /// <summary>The message severity / intent.</summary>
    public MessageKind Kind { get; init; } = MessageKind.Trace;

    /// <summary>The parts that make up the message.</summary>
    public ImmutableArray<ConsoleMessagePart> Parts { get; init; } = [];

    /// <summary>Whether the renderer should emit a trailing blank line after this message.</summary>
    public bool IsWithLineBreak { get; init; }

    /// <summary>Requests a trailing blank line after the message.</summary>
    public ConsoleMessageBuilder WithLineBreak(bool withLineBreak = true) => this with { IsWithLineBreak = withLineBreak };

    /// <summary>Sets the message <see cref="MessageKind"/>.</summary>
    public ConsoleMessageBuilder WithKind(MessageKind kind) => this with { Kind = kind };

    /// <summary>Adds a timestamp part.</summary>
    public ConsoleMessageBuilder WithTimestamp(DateTimeOffset timestamp) => WithUniquePart(ConsoleMessageTimestampPartFactory.CreateTimestampPart(timestamp));

    /// <summary>Adds a title part from literal text.</summary>
    public ConsoleMessageBuilder WithTitle(string title) => WithUniquePart(ConsoleMessageTitlePartFactory.CreateTitlePart(title));

    /// <summary>Adds a title part from a syntax error's diagnostic code.</summary>
    public ConsoleMessageBuilder WithTitle(VBSyntaxErrorInfo error) => WithUniquePart(ConsoleMessageTitlePartFactory.CreateTitlePart(error.ToDiagnosticCode()));

    /// <summary>Adds a title part from a compile error's diagnostic code.</summary>
    public ConsoleMessageBuilder WithTitle(VBCompileErrorInfo error) => WithUniquePart(ConsoleMessageTitlePartFactory.CreateTitlePart(error.ToDiagnosticCode()));

    /// <summary>Adds a title part from a runtime error's diagnostic code.</summary>
    public ConsoleMessageBuilder WithTitle(VBRuntimeErrorInfo error) => WithUniquePart(ConsoleMessageTitlePartFactory.CreateTitlePart(error.ToDiagnosticCode()));

    /// <summary>Adds a title part from an application error's diagnostic code.</summary>
    public ConsoleMessageBuilder WithTitle(VBApplicationErrorInfo error) => WithUniquePart(ConsoleMessageTitlePartFactory.CreateTitlePart(error.ToDiagnosticCode()));

    /// <summary>Adds a title part from an exception's type name.</summary>
    public ConsoleMessageBuilder WithTitle(Exception exception) => WithUniquePart(ConsoleMessageTitlePartFactory.CreateTitlePart(exception.GetType().Name));

    /// <summary>Adds a body part from literal text.</summary>
    public ConsoleMessageBuilder WithMessageBody(string body) => WithUniquePart(ConsoleMessageBodyPartFactory.CreateMessageBodyPart(body));

    /// <summary>Adds a body part from an exception's message.</summary>
    public ConsoleMessageBuilder WithMessageBody(Exception exception) => WithUniquePart(ConsoleMessageBodyPartFactory.CreateMessageBodyPart(exception));

    /// <summary>Adds a body part from an error's description.</summary>
    public ConsoleMessageBuilder WithMessageBody(VBErrorInfo error) => WithUniquePart(ConsoleMessageBodyPartFactory.CreateMessageBodyPart(error.Description));

    /// <summary>Adds a verbose-detail part from literal text.</summary>
    public ConsoleMessageBuilder WithVerbose(string verbose) => WithUniquePart(ConsoleMessageVerbosePartFactory.CreateVerbosePart(verbose));

    /// <summary>Adds a verbose-detail part from an error's verbose text.</summary>
    public ConsoleMessageBuilder WithVerbose(VBErrorInfo error) => WithUniquePart(ConsoleMessageVerbosePartFactory.CreateVerbosePart(error.Verbose));

    /// <summary>Adds a stack-trace part for an exception.</summary>
    public ConsoleMessageBuilder WithStackTrace(Exception exception) => WithUniquePart(ConsoleMessageStackTracePartFactory.CreateStackTracePart(exception));

    /// <summary>Adds a numeric placeholder span, substituted where <c>{$name}</c> appears in the body.</summary>
    public ConsoleMessageBuilder WithPlaceholder(PlaceholderKind kind, string placeholder, double value) => WithPart(ConsoleMessageMetricPartFactory.CreatePlaceholderPart(kind, $"{{${placeholder}}}", value));

    /// <summary>Adds a string placeholder span, substituted where <c>{$name}</c> appears in the body.</summary>
    public ConsoleMessageBuilder WithPlaceholder(string placeholder, string value) => WithPart(ConsoleMessageMetricPartFactory.CreatePlaceholderPart($"{{${placeholder}}}", value));

    /// <summary>Adds an arbitrary part.</summary>
    public ConsoleMessageBuilder WithPart(ConsoleMessagePart part) => this with { Parts = [.. Parts, part] };

    private ConsoleMessageBuilder WithUniquePart<TPart>(TPart part) where TPart : ConsoleMessagePart
        => Parts.Any(existing => existing.Part == part.Part) ? this : WithPart(part);

    /// <summary>The title part's text, or <see cref="string.Empty"/>.</summary>
    public string Title => Parts.LastOrDefault(part => part.Part == MessagePart.Title)?.Value ?? string.Empty;

    /// <summary>The body part's text, or <see cref="string.Empty"/>.</summary>
    public string Body => Parts.LastOrDefault(part => part.Part == MessagePart.Body)?.Value ?? string.Empty;

    /// <summary>The verbose part's text, or <see cref="string.Empty"/>.</summary>
    public string Verbose => Parts.LastOrDefault(part => part.Part == MessagePart.Verbose)?.Value ?? string.Empty;

    /// <summary>The stack-trace part's text, or <see cref="string.Empty"/>.</summary>
    public string StackTrace => Parts.LastOrDefault(part => part.Part == MessagePart.StackTrace)?.Value ?? string.Empty;
}
