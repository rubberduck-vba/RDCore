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
    /// Pushes <paramref name="frame"/> onto the call stack, making it the current frame.
    /// </summary>
    bool TryPush(ICallStackFrame frame);

    /// <summary>
    /// Pops the current frame off the call stack, freeing every locally-scoped symbol it allocated.
    /// </summary>
    bool TryPop([NotNullWhen(true)] out ICallStackFrame? frame);
}
