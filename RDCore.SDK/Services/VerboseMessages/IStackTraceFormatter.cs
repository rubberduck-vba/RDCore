using RDCore.SDK.Semantics.Runtime.Abstract;
using System.Text;

namespace RDCore.SDK.Services.Formatters;

public interface IStackTraceFormatter
{
    void Format(StringBuilder builder, IEnumerable<IStackFrame> stack);
}

public interface IStackTraceFormatter<T> : IStackTraceFormatter
    where T : struct, Enum
{
    void Format(StringBuilder builder, IEnumerable<IStackFrame<T>> stack);
}
