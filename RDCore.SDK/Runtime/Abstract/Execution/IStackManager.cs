namespace RDCore.SDK.Runtime.Abstract.Execution;

public interface IStackManager<TFrame> where TFrame : IStackFrame, IEquatable<TFrame>
{
    /// <summary>
    /// Clears the managed stack.
    /// </summary>
    void Clear();

    /// <summary>
    /// Pushes the specified frame to the managed stack.
    /// </summary>
    /// <param name="frame">The stack frame to be pushed.</param>
    /// <returns><c>true</c> if the frame was successfully pushed, <c>false</c> otherwise.</returns>
    bool TryPush(TFrame frame);

    /// <summary>
    /// Pops the top-most frame from the managed stack.
    /// </summary>
    /// <param name="frame">The retrieved stack frame, if successful.</param>
    /// <returns><c>true</c> if the frame was successfully popped, <c>false</c> otherwise.</returns>
    bool TryPop(out TFrame frame);
}
