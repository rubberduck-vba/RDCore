using System.Diagnostics;

namespace RDCore.SDK.Runtime.Shared;

public sealed class LetCoercionStackManager : StackManager<LetCoercionStackFrame>
{
    private readonly HashSet<LetCoercionStackFrame> _frameHash = [];

    protected sealed override void OnStackCleared() => _frameHash.Clear();
    
    protected sealed override bool OnBeforeTryPush(LetCoercionStackFrame frame) => !_frameHash.Contains(frame);
    protected sealed override void OnPushed(LetCoercionStackFrame frame)
    {
        _frameHash.Add(frame);
        Debug.Assert(_frameHash.Count == Depth);
    }

    protected sealed override bool OnBeforeTryPop() => _frameHash.Count > 0;

    protected sealed override void OnFramePopped(LetCoercionStackFrame frame)
    {
        Debug.Assert(_frameHash.Contains(frame));
        _frameHash.Remove(frame);

        Debug.Assert(_frameHash.Count == Depth);
    }
}
