using RDCore.SDK.Model.AST.Abstract;
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
/// <param name="EnclosingWithTargetExpression">
/// The expression the enclosing <c>With</c> block's target was written as, which says how it is declared: an object a
/// <c>With</c> holds is a value, and a value carries the object and nothing of the type its variable was declared as. What
/// a with-relative member is looked up in - a class, or an interface of it (<strong>MS-VBAL §5.3.1.9</strong>) - is decided by that.
/// </param>
/// <param name="ConditionalConstant">
/// The value of the module's own conditional-compilation constant of that name (<strong>MS-VBAL §3.4.1</strong>), or <see langword="null"/> when the module
/// declares none: what a <c>cc-expression</c> that names one is evaluated with, ahead of the project-level constants that the module's own shadow.
/// <see langword="null"/> - the default - states no module: only the project's constants are there.
/// </param>
public readonly record struct RuntimeEvaluationContext(
    Uri Scope, VBTypedValue? EnclosingWithTarget = null, ExpressionNode? EnclosingWithTargetExpression = null,
    Func<string, VBTypedValue?>? ConditionalConstant = null);
