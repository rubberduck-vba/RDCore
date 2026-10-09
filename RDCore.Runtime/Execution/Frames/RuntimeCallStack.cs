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

    private CallStackFrame? _selected;
    private int _selectedDepth;

    /// <inheritdoc cref="ICallStack.Current"/>
    /// <remarks>
    /// The activation on top of the stack - unless one was <see cref="Select"/>ed, and nothing has been pushed since: then the one that was.
    /// </remarks>
    public CallStackFrame? Current
        => _selected is not null && Frames.Count == _selectedDepth ? _selected : Frames.Count > 0 ? Frames.First() : null;

    /// <summary>
    /// Makes an activation the current one, for as long as the scope lives and until something is called: whatever runs meanwhile sees that activation's variables as if
    /// it were running in it. It is how an expression is evaluated in an activation that is not the innermost.
    /// </summary>
    /// <remarks>
    /// A call made meanwhile pushes its own activation, which is the current one while it runs; when it returns, the selected one is again. The program must be
    /// waiting - the stack is not otherwise something to look at from outside.
    /// </remarks>
    /// <param name="frame">An activation that is on the stack.</param>
    /// <returns>The scope; disposing it puts the top of the stack back.</returns>
    public IDisposable Select(CallStackFrame frame)
    {
        (_selected, _selectedDepth) = (frame, Frames.Count);
        return new Selection(this);
    }

    private sealed class Selection(RuntimeCallStack stack) : IDisposable
    {
        public void Dispose() => stack._selected = null;
    }

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
