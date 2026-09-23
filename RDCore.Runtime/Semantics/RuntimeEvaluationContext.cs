using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.Runtime.Semantics;

/// <summary>
/// The runtime context a <see cref="RuntimeExpressionEvaluator"/> evaluates an expression against —
/// the runtime analogue of <c>StaticEvaluationContext</c>.
/// </summary>
/// <param name="Scope">
/// The scope a <c>SimpleName</c>, <c>Me</c>, <c>New</c>, or with-relative <c>MemberAccess</c>/
/// <c>DictionaryAccess</c> resolves from — the currently executing procedure's own <c>Uri</c>. Not
/// derived from the current call-stack frame: <c>ICallStackFrame.StaticSymbol</c> carries no scope
/// <c>Uri</c> of its own (it is always anchored to <c>StaticSymbol.GlobalUri</c>), so whatever creates
/// the frame — which already knows the real procedure <c>Uri</c> — is the one place this can come from
/// today, pending the procedure-invocation machinery that would let a frame carry it itself.
/// </param>
/// <param name="EnclosingWithTarget">
/// The innermost enclosing <c>With</c> block's target value (<strong>MS-VBAL §5.6.15</strong>), or
/// <c>null</c> when the expression being evaluated is not inside any <c>With</c> block. A with-relative
/// access (<c>.Member</c>/<c>!Member</c>) resolves against this.
/// </param>
public readonly record struct RuntimeEvaluationContext(Uri Scope, VBTypedValue? EnclosingWithTarget = null);
