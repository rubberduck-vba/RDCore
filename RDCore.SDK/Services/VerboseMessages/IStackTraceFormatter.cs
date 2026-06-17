using RDCore.SDK.Runtime.Abstract.Execution;
using System.Text;

namespace RDCore.SDK.Services.VerboseMessages;

public interface IStackTraceFormatter
{
    void Format(StringBuilder builder, IEnumerable<IStackFrame> stack);
}

public interface IStackTraceFormatter<T> : IStackTraceFormatter
    where T : struct, Enum
{
    void Format(StringBuilder builder, IEnumerable<IStackFrame<T>> stack);
}
