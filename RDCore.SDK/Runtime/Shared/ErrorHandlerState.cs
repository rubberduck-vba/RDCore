using RDCore.SDK.Model.Errors;

namespace RDCore.SDK.Runtime.Shared;

/// <summary>
/// An activation's current error-handling policy (<strong>MS-VBAL §5.4.4</strong>).
/// </summary>
public enum ErrorHandlingMode
{
    /// <summary>
    /// No handler is active — an <see cref="RDCore.SDK.Semantics.Instructions.InstructionKind"/>'s
    /// <c>Error</c> outcome propagates out of the activation unchanged. The policy every activation
    /// starts with, and the one an <c>On Error GoTo</c> handler's own catch resets to.
    /// </summary>
    Disabled,

    /// <summary>
    /// <c>On Error Resume Next</c>: an error is caught silently and execution continues with the
    /// statement right after the one that raised it. Unlike <see cref="GoTo"/>, catching one does not
    /// reset the policy — every later error in the same activation is caught the same way.
    /// </summary>
    ResumeNext,

    /// <summary>
    /// <c>On Error GoTo</c> &lt;label&gt;: an error branches to <see cref="ErrorHandlerState.HandlerTarget"/>.
    /// Catching one resets the policy to <see cref="Disabled"/> — a second, unhandled error inside the
    /// handler body itself propagates rather than re-entering the same handler.
    /// </summary>
    GoTo,
}

/// <summary>
/// The hidden per-activation state that gives <c>On Error</c>/<c>Resume</c> meaning
/// (<strong>MS-VBAL §5.4.4</strong>) — every activation carries exactly one of these (not one per
/// block-opening instruction, the way <c>With</c>/<c>Select</c>/<c>For</c> hidden state is), since an
/// <c>On Error</c> statement changes the activation's policy going forward, not a lexically-scoped value.
/// </summary>
/// <param name="Mode">The activation's current error-handling policy.</param>
/// <param name="HandlerTarget">
/// For <see cref="ErrorHandlingMode.GoTo"/>: the resolved offset to branch to on an error. Unused otherwise.
/// </param>
/// <param name="ActiveError">
/// The error a handler most recently caught, if any — cleared by a <c>Resume</c> statement. <c>Resume</c>
/// with no active error is <strong>MS-VBAL §5.4.4.2</strong> error 20, "Resume without error".
/// </param>
/// <param name="FaultStatementOffset">
/// The offset of the instruction whose execution raised <see cref="ActiveError"/> — what a bare
/// <c>Resume</c> re-executes, and what <c>Resume Next</c> continues past.
/// </param>
public readonly record struct ErrorHandlerState(ErrorHandlingMode Mode, int? HandlerTarget, VBRuntimeErrorInfo? ActiveError, int? FaultStatementOffset)
{
    /// <summary>
    /// The state every activation starts with: no handler active, no active error.
    /// </summary>
    public static readonly ErrorHandlerState Disabled = new(ErrorHandlingMode.Disabled, null, null, null);
}
