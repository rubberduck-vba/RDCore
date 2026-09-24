using RDCore.Runtime.Semantics;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Runtime.Execution;

/// <summary>
/// Matches a <c>Select Case</c>'s already-evaluated selector value against one <c>Case</c> block's range
/// clauses (<strong>MS-VBAL §5.4.2.10</strong>).
/// </summary>
/// <remarks>
/// Each range-clause shape is evaluated exactly the way the spec's own runtime semantics phrase it — as a
/// real comparison/logical expression, built from the selector (wrapped as a <see cref="LiteralExpressionNode"/>,
/// since it was already evaluated once and must not be re-evaluated per clause) and the clause's own
/// operand expression(s), through the real operator machinery
/// (<see cref="RuntimeExpressionEvaluator"/>/<see cref="RDCore.Runtime.Semantics.Operators.IOperatorRuntimeSemanticsProvider"/>)
/// — never a hand-rolled equality/range check.
/// </remarks>
public sealed class CaseMatchEvaluator(RuntimeExpressionEvaluator expressionEvaluator)
{
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
        CaseValueRangeClauseNode value => EvaluateComparison(session, Tokens.CompareEqualOp, selector, value.Value, context),

        // "the expression <select-expression> <comparison-operator> <expression> is evaluated"
        CaseComparisonRangeClauseNode comparison => EvaluateComparison(session, comparison.ComparisonOperator, selector, comparison.Value, context),

        // "the expression ((select-expression) >= (start-value)) And ((select-expression) <= (end-value)) is evaluated"
        CaseToRangeClauseNode range => EvaluateRange(session, selector, range, context),

        _ => RuntimeSemanticsEvaluationResult.InternalError(),
    };

    private RuntimeSemanticsEvaluationResult EvaluateComparison(IRuntimeSession session, string comparisonOperator, VBTypedValue selector, ExpressionNode operand, RuntimeEvaluationContext context)
    {
        var comparison = new VBBinaryOperatorExpressionNode(comparisonOperator, operand.Identity, operand.Location, SelectorLiteral(selector, operand), operand);
        return expressionEvaluator.Evaluate(session, comparison, context);
    }

    private RuntimeSemanticsEvaluationResult EvaluateRange(IRuntimeSession session, VBTypedValue selector, CaseToRangeClauseNode range, RuntimeEvaluationContext context)
    {
        var lowerBound = new VBBinaryOperatorExpressionNode(Tokens.CompareGreaterThanOrEqualOp, range.Identity, range.SourceLocation, SelectorLiteral(selector, range.Start), range.Start);
        var upperBound = new VBBinaryOperatorExpressionNode(Tokens.CompareLessThanOrEqualOp, range.Identity, range.SourceLocation, SelectorLiteral(selector, range.End), range.End);
        var inRange = new VBBinaryOperatorExpressionNode(Tokens.LogicalAndOp, range.Identity, range.SourceLocation, lowerBound, upperBound);
        return expressionEvaluator.Evaluate(session, inRange, context);
    }

    private static LiteralExpressionNode SelectorLiteral(VBTypedValue selector, ExpressionNode operand) => new(operand.Identity, operand.Location, selector);
}
