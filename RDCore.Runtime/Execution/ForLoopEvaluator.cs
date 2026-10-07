using RDCore.Runtime.Semantics;
using RDCore.Runtime.Semantics.Operators;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Symbols.Operators;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Runtime.Execution;

/// <summary>
/// Evaluates a <c>For</c> loop's own operand expressions, counter assignment, increment, and range test —
/// <strong>MS-VBAL §5.4.2.3</strong>.
/// </summary>
/// <remarks>
/// Every step is the real runtime semantics VBA source itself would go through — Let-assignment, addition, the
/// relational operators (<see cref="IOperatorRuntimeSemanticsProvider"/>) — never a hand-rolled counter/bound
/// comparison. The location-bearing node passed to each is always a real node the loop already has: the loop's
/// own counter expression, or (for the counter's initial assignment) the loop's own start expression — never a
/// synthetic stand-in.
/// </remarks>
public sealed class ForLoopEvaluator(RuntimeExpressionEvaluator expressionEvaluator, IOperatorRuntimeSemanticsProvider operators)
{
    /// <summary>
    /// Evaluates a single operand expression (<c>start-value</c>, <c>end-value</c>, <c>step-increment</c>).
    /// </summary>
    public RuntimeSemanticsEvaluationResult EvaluateOperand(IRuntimeSession session, ExpressionNode expression, RuntimeEvaluationContext context)
        => expressionEvaluator.Evaluate(session, expression, context);

    /// <summary>
    /// Let-assigns the loop's counter symbol to <paramref name="value"/>, returning its own (possibly
    /// coerced) resulting value — MS-VBAL comparisons and the next increment both operate on this, never
    /// on the raw <paramref name="value"/> that was assigned.
    /// </summary>
    public RuntimeSemanticsEvaluationResult AssignCounter(IRuntimeSession session, ForLoopState state, ExpressionNode locationNode, VBTypedValue value)
    {
        var syntheticOperator = new VBBinaryOperatorExpressionNode(OperatorSymbolNames.BinaryAssignmentValueOp, locationNode.Identity, locationNode.Location, locationNode, locationNode);
        return operators.EvaluateBinaryOperator(session, syntheticOperator, new VBSymbolDescValue(state.Counter), value);
    }

    /// <summary>
    /// <c>counter + step</c> (<strong>MS-VBAL §5.6.9.3</strong>) — the value to Let-assign back to the
    /// counter on every <c>Next</c>.
    /// </summary>
    public RuntimeSemanticsEvaluationResult Increment(IRuntimeSession session, ForLoopState state, VBTypedValue counter)
        => operators.EvaluateBinaryOperator(session, Tokens.AdditionOp, state.ControlExpression, counter, state.Step);

    /// <summary>
    /// Whether the loop has run out of range and should complete — steps 1/2 of the algorithm: a
    /// non-negative <c>step-increment</c> completes when the counter exceeds <c>end-value</c>; a negative
    /// one completes when the counter falls below it.
    /// </summary>
    public RuntimeSemanticsEvaluationResult IsOutOfRange(IRuntimeSession session, ForLoopState state, VBTypedValue counter)
        => operators.EvaluateBinaryOperator(session,
            ((VBNumericTypedValue)state.Step).AsDouble < 0 ? Tokens.CompareLessThanOp : Tokens.CompareGreaterThanOp,
            state.ControlExpression, counter, state.End);
}
