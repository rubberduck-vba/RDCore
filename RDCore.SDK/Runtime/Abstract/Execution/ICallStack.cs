using System.Diagnostics.CodeAnalysis;

namespace RDCore.SDK.Runtime.Abstract.Execution;

/// <summary>
/// The <em>call stack</em> of an execution session: an ordered stack of <see cref="ICallStackFrame"/>
/// activation records, top-most (current) frame first (<strong>RD-VBAL §2.3.1.2</strong>).
/// </summary>
/// <remarks>
/// ⚖️<strong>RDCore</strong> provides an implementation of this interface <strong>licensed under GPLv3</strong>.
/// </remarks>
public interface ICallStack
{
    /// <summary>
    /// The current stack depth.
    /// </summary>
    int Depth { get; }

    /// <summary>
    /// The top-most (current) frame, or <c>null</c> if the call stack is empty.
    /// </summary>
    ICallStackFrame? Current { get; }

    /// <summary>
    /// Every frame on the stack, top-most (current) first.
    /// </summary>
    /// <remarks>
    /// A read-only view, for the things that need the whole chain rather than its top: capturing the
    /// <see cref="Model.Errors.VBStackTrace"/> of a run-time error, chiefly. Enumerating pops nothing —
    /// every frame is still the stack's, and its storage is still live.
    /// </remarks>
    IEnumerable<ICallStackFrame> Frames { get; }

    /// <summary>
    /// Pushes <paramref name="frame"/> onto the call stack, making it the current frame.
    /// </summary>
    bool TryPush(ICallStackFrame frame);

    /// <summary>
    /// Pops the current frame off the call stack, freeing every locally-scoped symbol it allocated.
    /// </summary>
    bool TryPop([NotNullWhen(true)] out ICallStackFrame? frame);
}
