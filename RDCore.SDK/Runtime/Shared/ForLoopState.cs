using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Runtime.Shared;

/// <summary>
/// The hidden per-activation state a <c>For</c> loop's opener stashes for its own closer to read back
/// (<strong>MS-VBAL §5.4.2.3</strong>) — the end/step values are evaluated once, ahead of the loop, and
/// read again on every <c>Next</c> without re-evaluating their source expressions.
/// </summary>
/// <param name="Counter">The <c>bound-variable-expression</c> symbol the loop counts through.</param>
/// <param name="ControlExpression">
/// The counter's own expression node, reused as the location-bearing node for every arithmetic/
/// let-assignment step a <c>Next</c> performs — never a synthetic stand-in.
/// </param>
/// <param name="End">The loop's <c>end-value</c>, evaluated once and never re-evaluated.</param>
/// <param name="Step">The loop's <c>step-increment</c>, evaluated once and never re-evaluated.</param>
public readonly record struct ForLoopState(Symbol Counter, ExpressionNode ControlExpression, VBTypedValue End, VBTypedValue Step);
