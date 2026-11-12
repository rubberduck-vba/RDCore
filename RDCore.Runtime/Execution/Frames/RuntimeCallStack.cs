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
    public const int ProgramThreadStackSize = SDK.Platform.LargeStackThread.StackSize;

    private CallStackFrame? _selected;
    private int _selectedDepth;

    // the activations compiled code made of the calls it made directly that have no frame yet (IR lowering S2.3b2), bottom first: all of them above every
    // frame, since a frame pushed above one makes the frames of them all first.
    private ILightActivation[] _lights = [];
    private int _lightCount;
    private bool _materializing;

    // the activation that last left each place of the stack an activation without a frame can be put in, which the next one put there may be made of again
    // (TakeSpare): as many as the stack was ever deep in them, whatever many calls were made - not one for each call of a recursion. Let go of when the
    // program ends (OnFramePopped).
    private ILightActivation?[] _spares = [];

    /// <summary>How many activations are on the stack, with those compiled code made without a frame.</summary>
    public new int Depth => base.Depth + _lightCount;

    // how deep the stack was when each event of a server's object that is being handled began, outermost first: the activation at that depth is its handler's.
    private readonly List<int> _eventFloors = [];

    /// <summary>
    /// Says that the handlers of an event a server's object raised run from now on, above the activations on the stack, until the returned scope is disposed.
    /// </summary>
    /// <returns>The scope of the event.</returns>
    internal IDisposable EventBegins()
    {
        var floor = Depth;
        lock (_eventFloors)
        {
            _eventFloors.Add(floor);
        }

        return new EventScope(this, floor);
    }

    /// <summary>
    /// Whether the activation at <paramref name="depth"/>, counted from the bottom of the stack from <c>0</c>, is the first of the handlers of an event a server's object
    /// raised: the activations above it are the event's, and the one below it is in the call the event was raised by.
    /// </summary>
    /// <param name="depth">The activation's place, from the bottom.</param>
    public bool BeginsAnEvent(int depth)
    {
        lock (_eventFloors)
        {
            return _eventFloors.Contains(depth);
        }
    }

    private sealed class EventScope(RuntimeCallStack stack, int floor) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                lock (stack._eventFloors)
                {
                    _ = stack._eventFloors.Remove(floor);
                }
            }
        }
    }

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
            Array.Resize(ref _spares, _lights.Length);
        }

        _lights[_lightCount++] = activation;
        return true;
    }

    /// <summary>
    /// The activation that last left the place of the stack the next activation without a frame goes in, when it left it returning, without a frame: the
    /// next one may be it, begun again. It is taken: an activation that is on the stack is the spare of no place, so none is made again while it runs.
    /// </summary>
    /// <returns><see langword="null"/> when none is.</returns>
    internal ILightActivation? TakeSpare()
    {
        if (_lightCount >= _spares.Length || _spares[_lightCount] is not { } spare)
        {
            return null;
        }

        _spares[_lightCount] = null;
        return spare;
    }

    /// <summary>
    /// Takes off the stack the activation on top of it, which compiled code put there without a frame, which still has none, and which returned: it is the
    /// spare of its place (<see cref="TakeSpare"/>).
    /// </summary>
    internal void PopLight()
    {
        var top = --_lightCount;
        _spares[top] = _lights[top];
        _lights[top] = null!;
    }

    /// <summary>
    /// Takes <paramref name="activation"/> off the stack when it is the activation without a frame on top of it, as a CLR exception goes by it: it is the spare
    /// of no place, since what it holds is what the exception left.
    /// </summary>
    /// <returns>Whether it was on top.</returns>
    internal bool TryPopLight(ILightActivation activation)
    {
        if (_lightCount == 0 || !ReferenceEquals(_lights[_lightCount - 1], activation))
        {
            return false;
        }

        _lights[--_lightCount] = null!;
        return true;
    }

    /// <summary>
    /// Gives each activation compiled code put on the stack without a frame its frame, bottom first, as a call through the invoker would have pushed it:
    /// whatever then looks at the stack - an error's trace, a debugger, the handler an error is looked for in, the directives of the current activation - sees
    /// every activation as the interpreter has it.
    /// </summary>
    /// <remarks>
    /// All of them or none: every frame is made before the stack changes, so one that can't be made leaves the stack as it was, every activation without a
    /// frame still on it, and counted once.
    /// </remarks>
    internal void MaterializeLights()
    {
        if (_lightCount == 0 || _materializing)
        {
            return;
        }

        var frames = new CallStackFrame[_lightCount];
        for (var i = 0; i < frames.Length; i++)
        {
            frames[i] = _lights[i].MakeFrame();
        }

        _materializing = true;
        try
        {
            _lightCount = 0;
            for (var i = 0; i < frames.Length; i++)
            {
                _ = base.TryPush(frames[i]);
                _lights[i].HeldIn(frames[i]);
                _lights[i] = null!;
            }
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
        Array.Clear(_spares);
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
    protected override void OnFramePopped(CallStackFrame? frame)
    {
        frame?.ReleaseAll();

        // the program ended: what it made of its compiled code is let go of with it, which a body loaded again since would otherwise keep.
        if (base.Depth == 0 && _lightCount == 0)
        {
            Array.Clear(_spares);
        }
    }

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
