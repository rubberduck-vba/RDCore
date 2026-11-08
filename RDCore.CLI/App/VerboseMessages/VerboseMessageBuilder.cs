using Microsoft.Extensions.Options;
using RDCore.SDK.Model.Errors;
using System.Text;

namespace RDCore.CLI.App.VerboseMessages;
/// <summary>
/// A service that formats the details of an error (<see cref="VBErrorDetails"/>) as the configurable verbose text the console shows.
/// </summary>
public interface IVerboseMessageBuilder
{
    /// <summary>
    /// Represents the specified details, their expression and their frames, as a localized, formatted <c>string</c>.
    /// </summary>
    /// <param name="details">
    /// The details of the error: their message is included when verbose output is enabled, regardless of other verbose configuration settings; their expression
    /// and their frames are formatted into the output per verbose configuration settings.
    /// </param>
    string Format(VBErrorDetails details);
}

/// <summary>
/// A service responsible for assembling a <c>Verbose</c> message string.
/// </summary>
public class VerboseMessageBuilder(
    IOptions<VerboseMessageOptions> options,
    IExpressionInfoFormatter expressionFormatter,
    IStackTraceFormatter stackTraceFormatter)
    : IVerboseMessageBuilder
{
    private readonly VerboseMessageOptions _config = options.Value;
    private readonly IExpressionInfoFormatter _expressionFormatter = expressionFormatter;
    private readonly IStackTraceFormatter _stackTraceFormatter = stackTraceFormatter;

    public string Format(VBErrorDetails details)
    {
        if (!_config.IsEnabled)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        FormatVerboseMessage(builder, details.Message);
        if (details.Expression is { } expression)
        {
            _expressionFormatter.Format(builder, expression);
        }
        _stackTraceFormatter.Format(builder, details.Frames);
        return builder.ToString();
    }

    /// <summary>
    /// Begins the verbose string by appending a <c>string</c> that should contain an optional, detailed, informative, implementation-defined message.
    /// </summary>
    protected virtual void FormatVerboseMessage(StringBuilder builder, string message)
    {
        if (options.Value.ShowMessageIcon)
        {
            builder.WithIcon(_config.MessageIcon);
        }
        builder.Append(message);
    }
}
