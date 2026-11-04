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

    /// <summary>
    /// The size of the stack of a thread a program runs on: room for <see cref="MaxDepth"/> activations, each of which is CLR calls of its own - about 13 KB of
    /// them for an interpreted activation of a Debug build, where a thread's default 1 MB holds fewer than 100. It is reserved, and committed only as it is used.
    /// </summary>
    public const int ProgramThreadStackSize = 256 * 1024 * 1024;

    private CallStackFrame? _selected;
    private int _selectedDepth;

    // the activations compiled code made of the calls it made directly that have no frame yet (IR lowering S2.3b2), bottom first: all of them above every
    // frame, since a frame pushed above one makes the frames of them all first.
    private ILightActivation[] _lights = [];
    private int _lightCount;
    private bool _materializing;

    /// <summary>How many activations are on the stack, with those compiled code made without a frame.</summary>
    public new int Depth => base.Depth + _lightCount;

    /// <summary>The activations on the stack, innermost first: those compiled code made without a frame are given theirs first.</summary>
    public new IReadOnlyCollection<CallStackFrame> Frames
    {
        get
        {
            MaterializeLights();
            return base.Frames;
        }
    }

    /// <summary>
    /// Puts an activation compiled code made of a call it made directly on the stack, without a frame: it is counted, and given its frame only when something
    /// is to see it (<see cref="MaterializeLights"/>).
    /// </summary>
    /// <returns>Whether there was room for it, as for a frame (<see cref="OnBeforeTryPush"/>).</returns>
    internal bool TryPushLight(ILightActivation activation)
    {
        if (Depth >= MaxDepth || !System.Runtime.CompilerServices.RuntimeHelpers.TryEnsureSufficientExecutionStack())
        {
            return false;
        }

        if (_lightCount == _lights.Length)
        {
            Array.Resize(ref _lights, Math.Max(16, _lights.Length * 2));
        }

        _lights[_lightCount++] = activation;
        return true;
    }

    /// <summary>Takes off the stack the activation on top of it, which compiled code put there without a frame and which still has none.</summary>
    internal void PopLight()
    {
        _lights[--_lightCount] = null!;
    }

    /// <summary>
    /// Gives each activation compiled code put on the stack without a frame its frame, bottom first, as a call through the invoker would have pushed it:
    /// whatever then looks at the stack - an error's trace, a debugger, the handler an error is looked for in, the directives of the current activation - sees
    /// every activation as the interpreter has it.
    /// </summary>
    internal void MaterializeLights()
    {
        if (_lightCount == 0 || _materializing)
        {
            return;
        }

        _materializing = true;
        try
        {
            for (var i = 0; i < _lightCount; i++)
            {
                _ = base.TryPush(_lights[i].Materialize());
                _lights[i] = null!;
            }

            _lightCount = 0;
        }
        finally
        {
            _materializing = false;
        }
    }

    /// <inheritdoc/>
    protected override void OnStackCleared()
    {
        Array.Clear(_lights, 0, _lightCount);
        _lightCount = 0;
    }

    /// <inheritdoc cref="ICallStack.Current"/>
    /// <remarks>
    /// The activation on top of the stack - unless one was <see cref="Select"/>ed, and nothing has been pushed since: then the one that was.
    /// </remarks>
    public CallStackFrame? Current
    {
        get
        {
            MaterializeLights();
            return _selected is not null && Depth == _selectedDepth ? _selected : TryPeek(out var top) ? top : null;
        }
    }

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
    /// <remarks>
    /// No activation is pushed past <see cref="MaxDepth"/>, nor when the thread the program runs on is near the end of its own stack: an activation is made by
    /// CLR calls, which a stack too small for the depth would overflow, ending the host without an error. Either is error 28, which the program can handle.
    /// </remarks>
    protected override bool OnBeforeTryPush(CallStackFrame frame)
    {
        // the frames of the activations below: theirs, as they are given them, are pushed where they are.
        if (_materializing)
        {
            return true;
        }

        MaterializeLights();
        return Depth < MaxDepth && System.Runtime.CompilerServices.RuntimeHelpers.TryEnsureSufficientExecutionStack();
    }

    /// <inheritdoc/>
    protected override bool OnBeforeTryPop()
    {
        MaterializeLights();
        return true;
    }

    /// <inheritdoc/>
    protected override void OnFramePopped(CallStackFrame? frame) => frame?.ReleaseAll();

    int ICallStack.Depth => Depth;

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
