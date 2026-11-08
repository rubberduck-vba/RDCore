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

    /// <summary>
    /// Formats a single <em>stack frame</em>.
    /// </summary>
    /// <remarks>
    /// 🧩 The base implementation appends the <c>Verbose_FrameDetails</c> resource string followed by a whitespace,
    /// then appends the
    /// </remarks>
    protected virtual void FormatStackFrame(StringBuilder builder, VBErrorFrame frame, bool withValues = false)
    {
        builder
            .WithIndent(_options.StackFrameMarker)
            .Append(Resources.Verbose_FrameDetails)
            .WithWhitespace()
            .Append(frame.NodeId);

        FormatOperands(builder, frame, withValues);
    }

    /// <summary>
    /// Formats the frame inputs.
    /// </summary>
    /// <remarks>
    /// 🧩 The base implementation encloses the <c>FormatFrameInputs</c> content in parentheses.
    /// </remarks>
    protected virtual void FormatOperands(StringBuilder builder, VBErrorFrame frame, bool withValues = false) =>
        builder.WithEnclosed(
            prefix: "(",
            enclosed: sb =>
            {
                FormatFrameInputs(sb, frame, withValues);
                return sb;
            },
            postfix: ")");

    /// <summary>
    /// Formats all frame inputs into the provided <em>string builder</em>. This section is <em>enclosed</em> in parentheses.
    /// </summary>
    /// <remarks>
    /// 🧩 The base implementation appends the values icon if applicable, then iterates the inputs and appends them one by one via <c>FormatFrameInput</c>, separating them by a comma.
    /// </remarks>
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

    /// <summary>
    /// Formats the <em>list separator</em> between any given two <see cref="VBErrorOperand"/> <em>stack frame inputs</em>.
    /// </summary>
    /// <remarks>
    /// 🧩 Base implementation appends a comma followed by a single whitespace character.
    /// </remarks>
    protected virtual void FormatInputSeparator(StringBuilder builder) => builder.Append(", ");

    /// <summary>
    /// Formats an individual frame input into the provided <em>string builder</em>.
    /// </summary>
    /// <remarks>
    /// 🧩 Base implementation encloses the <see cref="string"/> repsentation of the operand value inside square brackets followed by a colon
    /// if values are shown and the operand has one (a type given to an operation has none), then appends the operand's type name.
    /// </remarks>
    protected virtual void FormatFrameInput(StringBuilder builder, VBErrorOperand operand, bool withValues = false)
    {
        builder
            .WithEnclosedIf(withValues && operand.Value is not null, "[", sb => sb.Append(operand.Value), "]:")
            .Append(operand.TypeName);
    }
}
