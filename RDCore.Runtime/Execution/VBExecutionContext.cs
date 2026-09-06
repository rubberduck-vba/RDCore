using RDCore.Runtime.Execution.Frames;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Runtime.Execution;

/// <summary>
/// Represents and encapsulates the execution environment for a bound node evaluation.
/// </summary>
public sealed class VBExecutionContext(IRuntimeSession session, StackManager<CallStackFrame> stack) : IVBExecutionContext
{
    public IRuntimeSession Session { get; } = session;
    private StackManager<CallStackFrame> Stack { get; } = stack;
}
