using RDCore.Runtime.Semantics;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.Runtime.Semantics.Operators;
using RDCore.Runtime.Semantics.Operators.Arithmetic;
using RDCore.Runtime.Semantics.Operators.Relational;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Symbols.Operators;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Runtime.Execution;

/// <summary>
/// Evaluates a <c>For</c> loop's own operand expressions, counter assignment, increment, and range test —
/// <strong>MS-VBAL §5.4.2.3</strong>.
/// </summary>
/// <remarks>
/// Every step is the real runtime semantics VBA source itself would go through — Let-assignment
/// (<see cref="BinaryLetAssignmentOperatorRuntimeSemantics"/>), addition
/// (<see cref="BinaryAdditionOperatorRuntimeSemantics"/>), the relational operators — never a hand-rolled
/// counter/bound comparison. The location-bearing node passed to each is always a real node the loop
/// already has: the loop's own counter expression, or (for the counter's initial assignment) the loop's
/// own start expression — never a synthetic stand-in.
/// </remarks>
public sealed class ForLoopEvaluator(RuntimeExpressionEvaluator expressionEvaluator, ILetCoercionRuntimeSemanticsProvider letCoercionProvider, IVerboseMessageBuilder formatterService)
{
    private readonly BinaryLetAssignmentOperatorRuntimeSemantics _letAssignment = new(letCoercionProvider, formatterService);
    private readonly BinaryAdditionOperatorRuntimeSemantics _addition = new(letCoercionProvider, formatterService);
    private readonly BinaryGtRelationalOperatorRuntimeSemantics _gt = new(letCoercionProvider, formatterService);
    private readonly BinaryLtRelationalOperatorRuntimeSemantics _lt = new(letCoercionProvider, formatterService);

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
        return _letAssignment.Evaluate(session, new(), syntheticOperator, new VBSymbolDescValue(state.Counter), value);
    }

    /// <summary>
    /// <c>counter + step</c> (<strong>MS-VBAL §5.6.9.3</strong>) — the value to Let-assign back to the
    /// counter on every <c>Next</c>.
    /// </summary>
    public RuntimeSemanticsEvaluationResult Increment(IRuntimeSession session, ForLoopState state, VBTypedValue counter)
        => _addition.Evaluate(session, new(), state.ControlExpression, counter, state.Step);

    /// <summary>
    /// Whether the loop has run out of range and should complete — steps 1/2 of the algorithm: a
    /// non-negative <c>step-increment</c> completes when the counter exceeds <c>end-value</c>; a negative
    /// one completes when the counter falls below it.
    /// </summary>
    public RuntimeSemanticsEvaluationResult IsOutOfRange(IRuntimeSession session, ForLoopState state, VBTypedValue counter)
        => ((VBNumericTypedValue)state.Step).AsDouble < 0
            ? _lt.Evaluate(session, new(), state.ControlExpression, counter, state.End)
            : _gt.Evaluate(session, new(), state.ControlExpression, counter, state.End);
}
