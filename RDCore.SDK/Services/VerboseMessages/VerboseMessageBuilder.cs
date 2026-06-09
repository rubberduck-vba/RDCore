using Microsoft.Extensions.Options;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Semantics.Runtime.Abstract;
using RDCore.SDK.Services.Formatters;
using System.Text;

namespace RDCore.SDK.Services.VerboseMessages;

/// <summary>
/// A service that builds the configurable <c>Verbose</c> message string of a <see cref="VBErrorInfo"/>.
/// </summary>
public interface IVerboseMessageBuilder
{
    /// <summary>
    /// Represents the specified <see cref="BoundExpression"/> and its stack trace as a localized, formatted <c>string</c>.
    /// </summary>
    /// <param name="message">A specific <em>verbose</em> message that is included when verbose output is enabled, regardless of other verbose configuration settings.</param>
    /// <param name="expression">The expression to be formatted into the output, per verbose configuration settings.</param>
    /// <param name="frames">The stack of evaluation frames to be formatted into the output, per verbose configuration settings.</param>
    string Format<TIndex>(string message, BoundExpressionNode expression, IEnumerable<IStackFrame<TIndex>> frames) where TIndex : struct, Enum;
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

    public string Format<TIndex>(string message, BoundExpressionNode expression, IEnumerable<IStackFrame<TIndex>> frames) where TIndex : struct, Enum
    {
        if (!_config.IsEnabled)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        FormatVerboseMessage(builder, message);
        _expressionFormatter.Format(builder, expression);
        _stackTraceFormatter.Format(builder, frames);
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
