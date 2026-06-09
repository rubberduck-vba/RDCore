using RDCore.Runtime.Model;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Runtime;
using RDCore.SDK.Semantics.Runtime.Abstract;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace RDCore.Runtime.Execution;

/// <summary>
/// Represents and encapsulates the execution environment and memory space.
/// </summary>
public sealed class VBExecutionContext(IVirtualHeap memory) : IVBExecutionContext
{
    required public bool Is64Bit { get; init; }

    IVirtualHeap IVBExecutionContext.Memory { get; } = memory;
    private IVirtualHeap Memory => ((IVBExecutionContext)this).Memory;


    public RuntimeSemanticsEvaluationResult Run()
    {
        // if we haven't returned yet, we never could.
        return RuntimeSemanticsEvaluationResult.InternalError(); // TODO throw 
    }

    public void AnalyzeCall()
    {
        // TODO analyze a Call context
        // 💡 analsis context should include a TimeSpan with the time elapsed during execution.
    }
}

/// <summary>
/// Manages a consistent <em>stack</em> of <see cref="IIndexedStackFrame"/> records.
/// </summary>
public interface IStackManager
{
    /// <summary>
    /// Pushes an <see cref="IIndexedStackFrame"/> onto the stack.
    /// </summary>
    /// <param name="frame">The <see cref="IIndexedStackFrame"/> to be pushed.</param>
    /// <returns><c>true</c> if the frame was successfully added.</returns>
    bool TryPush(IIndexedStackFrame frame);
}

public record class StackManager
{
    private Stack<IIndexedStackFrame> _frameStack { get; } = [];
    private HashSet<IIndexedStackFrame> _frameSet { get; } = [];

    public bool TryPush(IIndexedStackFrame frame)
    {
        if (_frameSet.Add(frame))
        {
            _frameStack.Push(frame);

            Debug.Assert(_frameSet.Count == _frameStack.Count);
            return true;
        }
        return false;
    }

    private bool TryPop([NotNullWhen(true)][MaybeNullWhen(false)] out IIndexedStackFrame? frame)
    {
        if (_frameStack.TryPop(out frame))
        {
            Debug.Assert(_frameSet.Contains(frame));
            _frameSet.Remove(frame);

            Debug.Assert(_frameSet.Count == _frameStack.Count);
            return true;
        }
        return false;
    }
}