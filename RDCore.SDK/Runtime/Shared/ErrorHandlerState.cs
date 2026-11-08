using RDCore.SDK.Model.Errors.Abstract;
using RDCore.SDK.Model.Errors;

namespace RDCore.SDK.Runtime.Shared;

/// <summary>
/// An activation's current error-handling policy (<strong>MS-VBAL §5.4.4</strong>).
/// </summary>
public enum ErrorHandlingMode
{
    /// <summary>
    /// No handler is active: an <see cref="RDCore.SDK.Semantics.Instructions.InstructionKind"/>'s
    /// <c>Error</c> outcome propagates out of the activation unchanged. The policy every activation
    /// starts with, and the one an <c>On Error GoTo</c> handler's own catch resets to.
    /// </summary>
    Disabled,

    /// <summary>
    /// <c>On Error Resume Next</c>: an error is caught silently and execution continues with the
    /// statement right after the one that raised it. Unlike <see cref="GoTo"/>, catching one does not
    /// reset the policy: every later error in the same activation is caught the same way.
    /// </summary>
    ResumeNext,

    /// <summary>
    /// <c>On Error GoTo</c> &lt;label&gt;: an error branches to <see cref="ErrorHandlerState.HandlerTarget"/>.
    /// Catching one resets the policy to <see cref="Disabled"/> while the handler runs (a second error inside the
    /// handler body itself propagates rather than re-entering the same handler, whatever <c>On Error</c> the handler runs), and a <c>Resume</c>, or
    /// an <c>On Error GoTo -1</c>, that ends the handler sets the policy to what it is to be after it (<see cref="ErrorHandlerState.AfterHandling"/>):
    /// <see cref="GoTo"/> the same label, unless the handler said otherwise.
    /// </summary>
    GoTo,
}

/// <summary>
/// The hidden per-activation state that gives <c>On Error</c>/<c>Resume</c> meaning
/// (<strong>MS-VBAL §5.4.4</strong>): every activation carries exactly one of these (not one per
/// block-opening instruction, the way <c>With</c>/<c>Select</c>/<c>For</c> hidden state is), since an
/// <c>On Error</c> statement changes the activation's policy going forward, not a lexically-scoped value.
/// </summary>
/// <param name="Mode">The activation's current error-handling policy.</param>
/// <param name="HandlerTarget">
/// For <see cref="ErrorHandlingMode.GoTo"/>: the resolved offset to branch to on an error. While a handler runs (<paramref name="HandlerEntry"/>), the
/// offset the policy after it branches to, when <paramref name="AfterHandling"/> is <see cref="ErrorHandlingMode.GoTo"/>. Unused otherwise.
/// </param>
/// <param name="ActiveError">
/// The error a handler most recently caught, if any: cleared by a <c>Resume</c> statement, or an <c>On Error GoTo -1</c>. <c>Resume</c>
/// with no active error is <strong>MS-VBAL §5.4.4.2</strong> error 20, "Resume without error".
/// </param>
/// <param name="HandlerEntry">
/// For an activation that is running the handler an <c>On Error GoTo</c> caught an error with: the offset of the handler, until a <c>Resume</c> or an
/// <c>On Error GoTo -1</c> ends it. Kept apart from <paramref name="HandlerTarget"/>, which is then where the next error goes once the handler is over, and
/// which an <c>On Error</c> statement the handler runs changes. <see langword="null"/> when no handler is running.
/// </param>
/// <param name="FaultStatementOffset">
/// The offset of the instruction whose execution raised <see cref="ActiveError"/>: what a bare
/// <c>Resume</c> re-executes, and what <c>Resume Next</c> continues past.
/// </param>
/// <param name="AfterHandling">
/// While a handler runs (<paramref name="HandlerEntry"/>): the policy once it is over - ended by a <c>Resume</c>, or an <c>On Error GoTo -1</c>. An
/// <c>On Error</c> statement the handler runs sets it, and nothing more: the error is still the one being handled, which the handler can still resume, and
/// a second error still propagates (MS-VBA, Excel-verified, oracle\s53). <see cref="ErrorHandlingMode.GoTo"/> the handler's own label until then.
/// </param>
public readonly record struct ErrorHandlerState(
    ErrorHandlingMode Mode, int? HandlerTarget, IVBRaisableError? ActiveError, int? FaultStatementOffset, int? HandlerEntry = null,
    ErrorHandlingMode AfterHandling = ErrorHandlingMode.GoTo)
{
    /// <summary>
    /// Whether a handler is running: an <c>On Error GoTo</c> caught an error, which no <c>Resume</c> or <c>On Error GoTo -1</c> ended yet.
    /// </summary>
    public bool IsHandling => Mode == ErrorHandlingMode.Disabled && ActiveError is not null && HandlerEntry is not null;

    /// <summary>
    /// The policy once the running handler is over (<see cref="AfterHandling"/>, <see cref="HandlerTarget"/>): no error active, and none being handled.
    /// When no handler is running (<see cref="IsHandling"/>), there is nothing to end: the state as it is.
    /// </summary>
    public ErrorHandlerState HandlingOver()
        => IsHandling ? new(AfterHandling, AfterHandling == ErrorHandlingMode.GoTo ? HandlerTarget : null, null, null) : this;

    /// <summary>
    /// The state every activation starts with: no handler active, no active error.
    /// </summary>
    public static readonly ErrorHandlerState Disabled = new(ErrorHandlingMode.Disabled, null, null, null);
}
