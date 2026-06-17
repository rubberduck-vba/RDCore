using RDCore.Runtime.Execution.Frames;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Runtime.Execution;

/// <summary>
/// Represents and encapsulates the execution environment and memory space.
/// </summary>
public sealed class VBExecutionContext(IVirtualHeap memory, StackManager<CallStackFrame> stack) : IVBExecutionContext
{
    required public bool Is64Bit { get; init; }

    IVirtualHeap IVBExecutionContext.Memory { get; } = memory;
    private IVirtualHeap Memory => ((IVBExecutionContext)this).Memory;
    private StackManager<CallStackFrame> Stack { get; } = stack;
}
