using RDCore.Runtime.Semantics;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.Runtime.Semantics.Operators;
using RDCore.Runtime.Semantics.Operators.Logical;
using RDCore.Runtime.Semantics.Operators.Relational;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Runtime.Execution;

/// <summary>
/// Matches a <c>Select Case</c>'s already-evaluated selector value against one <c>Case</c> block's range
/// clauses (<strong>MS-VBAL §5.4.2.10</strong>).
/// </summary>
/// <remarks>
/// Each range-clause shape is evaluated exactly the way the spec's own runtime semantics phrase it — as a
/// real comparison/logical expression built from the selector and the clause's own operand expression(s),
/// through the real operator machinery (<see cref="BinaryRelationalOperatorRuntimeSemantics"/>/
/// <see cref="BinaryAndLogicalOperatorRuntimeSemantics"/>) — never a hand-rolled equality/range check.
/// Calls the specific comparison/logical strategy directly, bypassing <see cref="OperatorRuntimeSemanticsProvider"/>'s
/// own token-dispatch layer (this class already knows exactly which one it wants) and, with it, the need
/// for a synthetic <c>VBBinaryOperatorExpressionNode</c> to carry a comparison that doesn't exist in
/// source: the location-bearing node passed to each strategy is always the clause's own real operand
/// expression, never a fabricated stand-in for the already-evaluated selector.
/// </remarks>
public sealed class CaseMatchEvaluator(RuntimeExpressionEvaluator expressionEvaluator, ILetCoercionRuntimeSemanticsProvider letCoercionProvider, IVerboseMessageBuilder formatterService)
{
    private readonly BinaryEqRelationalOperatorRuntimeSemantics _eq = new(letCoercionProvider, formatterService);
    private readonly BinaryNeqRelationalOperatorRuntimeSemantics _neq = new(letCoercionProvider, formatterService);
    private readonly BinaryGtRelationalOperatorRuntimeSemantics _gt = new(letCoercionProvider, formatterService);
    private readonly BinaryGtEqRelationalOperatorRuntimeSemantics _gtEq = new(letCoercionProvider, formatterService);
    private readonly BinaryLtRelationalOperatorRuntimeSemantics _lt = new(letCoercionProvider, formatterService);
    private readonly BinaryLtEqRelationalOperatorRuntimeSemantics _ltEq = new(letCoercionProvider, formatterService);
    private readonly BinaryAndLogicalOperatorRuntimeSemantics _and = new(letCoercionProvider, formatterService);

    /// <summary>
    /// Evaluates a <c>Select Case</c>'s own selector (<c>select-expression</c>) — once, ahead of every
    /// <c>Case</c> header's own range-clause matching against the result (<strong>MS-VBAL §5.4.2.10</strong>).
    /// </summary>
    public RuntimeSemanticsEvaluationResult EvaluateSelector(IRuntimeSession session, ExpressionNode controlExpression, RuntimeEvaluationContext context)
        => expressionEvaluator.Evaluate(session, controlExpression, context);

    /// <summary>
    /// Evaluates <paramref name="caseBlock"/>'s range clauses, in source order, against
    /// <paramref name="selector"/> — short-circuiting on the first clause that matches, per spec ("any
    /// subsequent range-clause in the case-clause is not evaluated").
    /// </summary>
    public RuntimeSemanticsEvaluationResult Evaluate(IRuntimeSession session, CaseExpressionStatementNode caseBlock, VBTypedValue selector, RuntimeEvaluationContext context)
    {
        foreach (var clause in caseBlock.RangeClauses)
        {
            var result = EvaluateClause(session, clause, selector, context);
            if (!result.IsSuccess)
            {
                return result;
            }
            if (((VBBooleanValue)result.Result!).Value.StoredValue != 0)
            {
                return result;
            }
        }
        return RuntimeSemanticsEvaluationResult.Success(VBBooleanValue.False);
    }

    private RuntimeSemanticsEvaluationResult EvaluateClause(IRuntimeSession session, CaseRangeClauseNode clause, VBTypedValue selector, RuntimeEvaluationContext context) => clause switch
    {
        // "the expression is evaluated and its result is compared with the value of select-expression" (5.4.2.10)
        CaseValueRangeClauseNode value => EvaluateComparison(session, _eq, selector, value.Value, context),

        // "the expression <select-expression> <comparison-operator> <expression> is evaluated"
        CaseComparisonRangeClauseNode comparison => EvaluateComparison(session, StrategyFor(comparison.ComparisonOperator), selector, comparison.Value, context),

        // "the expression ((select-expression) >= (start-value)) And ((select-expression) <= (end-value)) is evaluated"
        CaseToRangeClauseNode range => EvaluateRange(session, selector, range, context),

        _ => RuntimeSemanticsEvaluationResult.InternalError(),
    };

    private BinaryRelationalOperatorRuntimeSemantics StrategyFor(string comparisonOperator) => comparisonOperator switch
    {
        Tokens.CompareEqualOp => _eq,
        Tokens.CompareNotEqualOp => _neq,
        Tokens.CompareGreaterThanOp => _gt,
        Tokens.CompareGreaterThanOrEqualOp => _gtEq,
        Tokens.CompareLessThanOp => _lt,
        Tokens.CompareLessThanOrEqualOp => _ltEq,
        _ => _eq, // MS-VBAL's range-clause comparison-operator grammar never yields anything else.
    };

    // The selector was already evaluated once (by EvaluateSelector); operand is the clause's own real
    // expression, evaluated fresh. The location-bearing node passed to the strategy is always operand -
    // a real, source-derived node - never a stand-in fabricated for the already-known selector value.
    private RuntimeSemanticsEvaluationResult EvaluateComparison(IRuntimeSession session, BinaryRelationalOperatorRuntimeSemantics strategy, VBTypedValue selector, ExpressionNode operand, RuntimeEvaluationContext context)
    {
        var operandResult = expressionEvaluator.Evaluate(session, operand, context);
        if (!operandResult.IsSuccess)
        {
            return operandResult;
        }
        return strategy.Evaluate(session, new(), operand, selector, operandResult.Result!);
    }

    private RuntimeSemanticsEvaluationResult EvaluateRange(IRuntimeSession session, VBTypedValue selector, CaseToRangeClauseNode range, RuntimeEvaluationContext context)
    {
        var lowerBound = EvaluateComparison(session, _gtEq, selector, range.Start, context);
        if (!lowerBound.IsSuccess)
        {
            return lowerBound;
        }

        var upperBound = EvaluateComparison(session, _ltEq, selector, range.End, context);
        if (!upperBound.IsSuccess)
        {
            return upperBound;
        }

        return _and.Evaluate(session, new(), range.Start, lowerBound.Result!, upperBound.Result!);
    }
}
