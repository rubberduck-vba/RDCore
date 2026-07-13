using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics;
using System.Text;

namespace RDCore.SDK.Services.VerboseMessages;

public class LetCoercionTraceFormatter : DefaultStackTraceFormatter, IStackTraceFormatter<InputIndex>
{
    private readonly VerboseMessageOptions _options;

    public LetCoercionTraceFormatter(VerboseMessageOptions options) 
        : base(options)
    {
        _options = options;
    }

    public void Format(StringBuilder builder, IEnumerable<IStackFrame<InputIndex>> stack)
        => Format(builder, stack.Cast<IStackFrame>());

    protected override void FormatOperands(StringBuilder builder, IStackFrame frame, bool withValues = false)
        => FormatOperands(builder, (IStackFrame<InputIndex>)frame, withValues);

    private static void FormatOperands(StringBuilder builder, IStackFrame<InputIndex> frame, bool withValues = false) => 
        builder
            .WithEnclosedIf(withValues, "[", frame[InputIndex.CoercionSourceValue].ManagedValue.ToString() ?? "🦄", "]:")
            .Append(frame[InputIndex.CoercionSourceValue].TypeInfo.Name)
            .Append(" -> ")
            .Append(((VBTypeDescValue)frame[InputIndex.CoercionDestinationType]).Target.Name);
}
