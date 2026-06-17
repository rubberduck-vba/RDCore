using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using System.Text;

namespace RDCore.SDK.Services.VerboseMessages;

public class DefaultStackTraceFormatter : IStackTraceFormatter
{
    private readonly VerboseMessageOptions _options;

    public DefaultStackTraceFormatter(VerboseMessageOptions options)
    {
        _options = options;
    }

    public void Format(StringBuilder builder, IEnumerable<IStackFrame> stack)
    {
        if (_options.IsEnabled && _options.StackTrace)
        {
            builder
                .WithIconIf(_options.ShowStackTraceIcon, _options.StackTraceIcon)
                .Append(Exceptions.Verbose_FrameDetails);

            var showValues = _options.ShowValues;
            if (stack.FirstOrDefault() is IStackFrame topFrame)
            {
                FormatTopStackFrame(builder, topFrame, showValues);
                foreach (var frame in stack.Skip(1))
                {
                    FormatStackFrame(builder, frame, showValues);
                }
            }
        }
    }

    protected virtual void FormatTopStackFrame(StringBuilder builder, IStackFrame frame, bool withValues = false)
    {
        builder
            .WithIndent(_options.TopStackFrameMarker)
            .WithWhitespace()
            .Append(frame.StaticSymbol.Name);
    
        FormatOperands(builder, frame, withValues);
    }

    /// <summary>
    /// Formats a single <em>stack frame</em>.
    /// </summary>
    /// <remarks>
    /// 🧩 The base implementation appends the <c>Verbose_FrameDetails</c> resource string followed by a whitespace,
    /// then appends the 
    /// </remarks>
    protected virtual void FormatStackFrame(StringBuilder builder, IStackFrame frame, bool withValues = false)
    {
        builder
            .WithIndent(_options.StackFrameMarker)
            .Append(Exceptions.Verbose_FrameDetails)
            .WithWhitespace()
            .Append(frame.StaticSymbol.Name);
    
        FormatOperands(builder, frame, withValues);
    }

    /// <summary>
    /// Formats the frame inputs.
    /// </summary>
    /// <remarks>
    /// 🧩 The base implementation encloses the <c>FormatFrameInputs</c> content in parentheses.
    /// </remarks>
    protected virtual void FormatOperands(StringBuilder builder, IStackFrame frame, bool withValues = false) => 
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
    protected virtual void FormatFrameInputs(StringBuilder builder, IStackFrame frame, bool withValues)
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

    private void AddInputSeparatorAsNeeded(StringBuilder builder, IStackFrame frame, int inputs)
    {
        if (inputs < frame.Inputs.Length - 1)
        {
            FormatInputSeparator(builder);
        }
    }

    /// <summary>
    /// Formats the <em>list separator</em> between any given two <see cref="VBTypedValue"/> <em>stack frame inputs</em>.
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
    /// if values are shown, then appends the operand's <c>TypeInfo.Name</c>.
    /// </remarks>
    protected virtual void FormatFrameInput(StringBuilder builder, VBTypedValue operand, bool withValues = false)
    {
        builder
            .WithEnclosedIf(withValues, "[", sb => sb.Append(operand.BoxedValue), "]:")
            .Append(operand.TypeInfo.Name);
    }
}
