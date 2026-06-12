using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Semantics.Runtime.Abstract;
using RDCore.SDK.Services.VerboseMessages;
using System.Text;

namespace RDCore.SDK.Services.Formatters
{
    public enum LetCoercionInputs
    {
        SourceValue = 0,
        DestinationType = 1,
    }
    public class LetCoercionTraceFormatter : DefaultStackTraceFormatter, IStackTraceFormatter<LetCoercionInputs>
    {
        private readonly VerboseMessageOptions _options;

        public LetCoercionTraceFormatter(VerboseMessageOptions options) 
            : base(options)
        {
            _options = options;
        }

        public void Format(StringBuilder builder, IEnumerable<IStackFrame<LetCoercionInputs>> stack)
            => Format(builder, stack.Cast<IStackFrame>());

        protected override void FormatOperands(StringBuilder builder, IStackFrame frame, bool withValues = false)
            => FormatOperands(builder, (IStackFrame<LetCoercionInputs>)frame, withValues);

        private void FormatOperands(StringBuilder builder, IStackFrame<LetCoercionInputs> frame, bool withValues = false) => 
            builder
                .WithEnclosedIf(withValues, "[", frame[LetCoercionInputs.SourceValue].BoxedValue.ToString() ?? "🦄", "]:")
                .Append(frame[LetCoercionInputs.SourceValue].TypeInfo.Name)
                .Append(" -> ")
                .Append(((VBTypeDescValue)frame[LetCoercionInputs.DestinationType]).Target.Name);
    }
}
