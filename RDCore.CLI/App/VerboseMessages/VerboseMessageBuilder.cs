using Microsoft.Extensions.Options;
using RDCore.SDK.Model.Errors;
using System.Text;

namespace RDCore.CLI.App.VerboseMessages;
public interface IVerboseMessageBuilder
{
    string Format(VBErrorDetails details);
}

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

    protected virtual void FormatVerboseMessage(StringBuilder builder, string message)
    {
        if (options.Value.ShowMessageIcon)
        {
            builder.WithIcon(_config.MessageIcon);
        }
        builder.Append(message);
    }
}
