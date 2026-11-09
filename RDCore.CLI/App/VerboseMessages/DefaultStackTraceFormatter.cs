using RDCore.SDK.Model.Errors;
using System.Text;

namespace RDCore.CLI.App.VerboseMessages;

public class DefaultStackTraceFormatter : IStackTraceFormatter
{
    private readonly VerboseMessageOptions _options;

    public DefaultStackTraceFormatter(VerboseMessageOptions options)
    {
        _options = options;
    }

    public void Format(StringBuilder builder, IEnumerable<VBErrorFrame> stack)
    {
        if (_options.IsEnabled && _options.StackTrace)
        {
            builder
                .WithIconIf(_options.ShowStackTraceIcon, _options.StackTraceIcon)
                .Append(Resources.Verbose_FrameDetails);

            var showValues = _options.ShowValues;
            if (stack.FirstOrDefault() is VBErrorFrame topFrame)
            {
                FormatTopStackFrame(builder, topFrame, showValues);
                foreach (var frame in stack.Skip(1))
                {
                    FormatStackFrame(builder, frame, showValues);
                }
            }
        }
    }

    protected virtual void FormatTopStackFrame(StringBuilder builder, VBErrorFrame frame, bool withValues = false)
    {
        builder
            .WithIndent(_options.TopStackFrameMarker)
            .WithWhitespace()
            .Append(frame.NodeId);

        FormatOperands(builder, frame, withValues);
    }

    protected virtual void FormatStackFrame(StringBuilder builder, VBErrorFrame frame, bool withValues = false)
    {
        builder
            .WithIndent(_options.StackFrameMarker)
            .Append(Resources.Verbose_FrameDetails)
            .WithWhitespace()
            .Append(frame.NodeId);

        FormatOperands(builder, frame, withValues);
    }

    protected virtual void FormatOperands(StringBuilder builder, VBErrorFrame frame, bool withValues = false) =>
        builder.WithEnclosed(
            prefix: "(",
            enclosed: sb =>
            {
                FormatFrameInputs(sb, frame, withValues);
                return sb;
            },
            postfix: ")");

    protected virtual void FormatFrameInputs(StringBuilder builder, VBErrorFrame frame, bool withValues)
    {
        // values icon is shown independently of whether the runtime values are shown.
        builder.WithIcon(_options.ValuesIcon, _options.ShowValuesIcon);

        var inputs = 0;
        foreach (var input in frame.Inputs)
        {
            FormatFrameInput(builder, input, withValues);
            AddInputSeparatorAsNeeded(builder, frame, inputs);

            inputs++;
        }
    }

    private void AddInputSeparatorAsNeeded(StringBuilder builder, VBErrorFrame frame, int inputs)
    {
        if (inputs < frame.Inputs.Count - 1)
        {
            FormatInputSeparator(builder);
        }
    }

    protected virtual void FormatInputSeparator(StringBuilder builder) => builder.Append(", ");

    protected virtual void FormatFrameInput(StringBuilder builder, VBErrorOperand operand, bool withValues = false)
    {
        builder
            .WithEnclosedIf(withValues && operand.Value is not null, "[", sb => sb.Append(operand.Value), "]:")
            .Append(operand.TypeName);
    }
}
