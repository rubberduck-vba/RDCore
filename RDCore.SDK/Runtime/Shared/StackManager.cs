using RDCore.SDK.Runtime.Abstract.Execution;
using System.Diagnostics.CodeAnalysis;

namespace RDCore.SDK.Runtime.Shared;

/// <summary>
/// A base service class for managing a stack of frames.
/// </summary>
/// <typeparam name="TFrame">The type of <see cref="IStackFrame"/> <em>managed frames</em> being stacked.</typeparam>
public abstract class StackManager<TFrame>
    where TFrame : IStackFrame, IEquatable<TFrame>
{
    private readonly Stack<TFrame> _frameStack = [];

    /// <summary>
    /// Gets the current managed stack <em>depth</em>.
    /// </summary>
    public int Depth => _frameStack.Count;

    /// <summary>
    /// Clears the managed stack.
    /// </summary>
    public void Clear()
    {
        _frameStack.Clear();
        OnStackCleared();
    }

    /// <summary>
    /// 🧩 <c>override</c> this method to clean up any instance state in a derived class when the managed stack is cleared..
    /// </summary>
    protected virtual void OnStackCleared() { }

    /// <summary>
    /// Pushes the specified frame to the managed stack.
    /// </summary>
    /// <param name="frame">The stack frame to be pushed.</param>
    /// <returns><c>true</c> if the frame was successfully pushed, <c>false</c> otherwise.</returns>
    public bool TryPush(TFrame frame)
    {
        if (OnBeforeTryPush(frame))
        {
            _frameStack.Push(frame);
            OnPushed(frame);

            return true;
        }

        return false;
    }

    /// <summary>
    /// 🧩 <c>override</c> this method in a derived class to intercept and abort a frame push.
    /// </summary>
    /// <param name="frame">The stack frame to be pushed.</param>
    /// <returns><c>true</c> if the push can proceed, <c>false</c> if it's aborted.</returns>
    protected virtual bool OnBeforeTryPush(TFrame frame) => true;
    /// <summary>
    /// 🧩 <c>override</c> this method in a derived class to inject behavior whenever a frame has been pushed onto the managed stack.
    /// </summary>
    /// <param name="frame">The stack frame that was pushed.</param>
    protected virtual void OnPushed(TFrame frame) { }

    /// <summary>
    /// Pops the top-most frame from the managed stack.
    /// </summary>
    /// <param name="frame">The retrieved stack frame, if successful.</param>
    /// <returns><c>true</c> if the frame was successfully popped, <c>false</c> otherwise.</returns>
    public bool TryPop([NotNullWhen(true)]out TFrame? frame)
    {
        if (OnBeforeTryPop())
        {
            var result = _frameStack.TryPop(out frame);
            OnFramePopped(frame);

            return result;
        }

        frame = default;
        return false;
    }

    /// <summary>
    /// 🧩 <c>override</c> this method in a derived class to intercept and abort a frame pop.
    /// </summary>
    /// <returns><c>true</c> if the pop can proceed, <c>false</c> if it's aborted.</returns>
    protected virtual bool OnBeforeTryPop() => true;

    /// <summary>
    /// 🧩 <c>override</c> this method in a derived class to inject behavior whenever a frame is successfully popped from the managed stack.
    /// </summary>
    /// <param name="frame">The stack frame that was popped.</param>
    protected virtual void OnFramePopped(TFrame? frame) { }
}
