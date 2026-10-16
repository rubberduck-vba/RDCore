namespace RDCore.SDK.Runtime.Shared;

/// <summary>
/// The let-coercions being evaluated, innermost first: a coercion that would push a frame already on the stack is recursive.
/// </summary>
/// <remarks>
/// The stack is seldom more than a frame or two deep, and empty when most coercions begin, so it is searched rather than hashed: hashing a frame hashes
/// the value it coerces, which boxes it, three times for every coercion. A frame is only pushed when it is not on the stack already, so no two frames on
/// it are equal.
/// </remarks>
public sealed class LetCoercionStackManager : StackManager<LetCoercionStackFrame>
{
    protected sealed override bool OnBeforeTryPush(LetCoercionStackFrame frame) => !Contains(frame);

    protected sealed override bool OnBeforeTryPop() => Depth > 0;
}
