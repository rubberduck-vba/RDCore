using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using System.Diagnostics.CodeAnalysis;

namespace RDCore.Runtime.Execution.Frames;

/// <summary>
/// The runtime <see cref="ICallStack"/>: a <see cref="StackManager{TFrame}"/> of
/// <see cref="CallStackFrame"/> activation records. Popping a frame frees every locally-scoped symbol
/// it allocated (<see cref="CallStackFrame.ReleaseAll"/>) — a frame's storage never outlives the call
/// that owns it.
/// </summary>
public sealed class RuntimeCallStack : StackManager<CallStackFrame>, ICallStack
{
    // Real VBA's own stack limit is implementation-defined; this is a conservative bound, generous
    // enough for any legitimate call depth but low enough to fail fast (MS-VBAL error 28, "Out of stack
    // space") on runaway recursion well before the host process's own real stack would be at risk.
    private const int MaxDepth = 5000;

    /// <inheritdoc cref="ICallStack.Current"/>
    public CallStackFrame? Current => Frames.Count > 0 ? Frames.First() : null;

    /// <inheritdoc/>
    protected override bool OnBeforeTryPush(CallStackFrame frame) => Depth < MaxDepth;

    /// <inheritdoc/>
    protected override void OnFramePopped(CallStackFrame? frame) => frame?.ReleaseAll();

    ICallStackFrame? ICallStack.Current => Current;

    IEnumerable<ICallStackFrame> ICallStack.Frames => Frames;

    bool ICallStack.TryPush(ICallStackFrame frame)
        => frame is CallStackFrame concrete && TryPush(concrete);

    bool ICallStack.TryPop([NotNullWhen(true)] out ICallStackFrame? frame)
    {
        var result = TryPop(out var concrete);
        frame = concrete;
        return result;
    }
}
