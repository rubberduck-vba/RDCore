using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.Runtime.Semantics.Operators;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Facts;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Semantics.Plans;

namespace RDCore.Runtime.Execution;

/// <summary>
/// Matches a <c>Select Case</c>'s already-evaluated selector value against one <c>Case</c> block's range
/// clauses (<strong>MS-VBAL §5.4.2.10</strong>).
/// </summary>
/// <remarks>
/// Each range-clause shape is evaluated the way the spec's own runtime semantics phrase it — as a
/// real comparison/logical expression built from the selector and the clause's own operand expression(s),
/// through the operator semantics (<see cref="IOperatorRuntimeSemanticsProvider"/>) — never a hand-rolled
/// equality/range check. The comparison has no operator expression of its own in source: the
/// location-bearing node it is evaluated for is always the clause's own real operand expression, never a
/// fabricated stand-in for the already-evaluated selector.
/// <para>
/// One departure, MS-VBA's (<strong>RD-VBAL §5.4.2.10</strong>): the value of a clause is let-coerced to the declared type of the selector before it is
/// compared with it, so that the two are compared in that type. A <c>Variant</c> selector is compared with the value as it is.
/// </para>
/// </remarks>
/// <param name="expressionEvaluator">Evaluates the selector and the values of the clauses.</param>
/// <param name="operators">The relational and logical operators each clause is matched with.</param>
/// <param name="letCoercion">Converts the value of a clause to the declared type of the selector.</param>
/// <param name="observation">Told what a clause is (<see cref="CaseClauseStatementFact"/>), when it is evaluated for an analysis.</param>
public sealed class CaseMatchEvaluator(
    RuntimeExpressionEvaluator expressionEvaluator, IOperatorRuntimeSemanticsProvider operators, ILetCoercionRuntimeSemanticsProvider letCoercion,
    AnalysisObservation? observation = null)
{
    /// <summary>
    /// Evaluates a <c>Select Case</c>'s own selector (<c>select-expression</c>) — once, ahead of every
    /// <c>Case</c> header's own range-clause matching against the result (<strong>MS-VBAL §5.4.2.10</strong>).
    /// </summary>
    public RuntimeSemanticsEvaluationResult EvaluateSelector(IRuntimeSession session, ExpressionNode controlExpression, RuntimeEvaluationContext context)
        => expressionEvaluator.Evaluate(session, controlExpression, context);

    /// <summary>
    /// <see cref="EvaluateSelector"/> on payloads (IR lowering S1.6), when the selector is an expression evaluated on them.
    /// </summary>
    /// <returns><see langword="false"/> when the selector is to be evaluated the way it always is.</returns>
    internal bool TryEvaluateSelectorUnboxed(IRuntimeSession session, ExpressionNode controlExpression, RuntimeEvaluationContext context, out ScalarPayload selector)
        => expressionEvaluator.TryEvaluateSelectorUnboxed(session, controlExpression, context, out selector);

    /// <summary>
    /// <see cref="Evaluate"/> on payloads, for a selector evaluated on them: each clause's comparison made as <see cref="Evaluate"/> makes it, in the same
    /// order, short-circuiting the same way.
    /// </summary>
    /// <returns><see langword="false"/> when a clause is not compared on payloads: the whole <c>Case</c> is then matched the way it always is, which
    /// evaluates again only clauses that have no other effect.</returns>
    internal bool TryMatchUnboxed(IRuntimeSession session, CaseExpressionStatementNode caseBlock, ScalarPayload selector, RuntimeEvaluationContext context, out bool isMatch)
    {
        isMatch = false;
        foreach (var clause in caseBlock.RangeClauses)
        {
            var compared = clause switch
            {
                CaseValueRangeClauseNode value => expressionEvaluator.TryCompareUnboxed(session, Tokens.CompareEqualOp, selector, value.Value, context, out isMatch),
                CaseComparisonRangeClauseNode comparison
                    => expressionEvaluator.TryCompareUnboxed(session, comparison.ComparisonOperator, selector, comparison.Value, context, out isMatch),
                CaseToRangeClauseNode range => TryMatchRangeUnboxed(session, range, selector, context, out isMatch),
                _ => false,
            };

            if (!compared)
            {
                return false;
            }

            if (isMatch)
            {
                return true;
            }
        }

        return true;
    }

    // both ends are compared, as the And of the two comparisons (EvaluateRange) evaluates both.
    private bool TryMatchRangeUnboxed(IRuntimeSession session, CaseToRangeClauseNode range, ScalarPayload selector, RuntimeEvaluationContext context, out bool isMatch)
    {
        isMatch = false;
        if (!expressionEvaluator.TryCompareUnboxed(session, Tokens.CompareGreaterThanOrEqualOp, selector, range.Start, context, out var above)
            || !expressionEvaluator.TryCompareUnboxed(session, Tokens.CompareLessThanOrEqualOp, selector, range.End, context, out var below))
        {
            return false;
        }

        isMatch = above && below;
        return true;
    }

    /// <summary>
    /// Evaluates <paramref name="caseBlock"/>'s range clauses, in source order, against
    /// <paramref name="selector"/> — short-circuiting on the first clause that matches, per spec ("any
    /// subsequent range-clause in the case-clause is not evaluated").
    /// </summary>
    /// <param name="session">The session the clauses are evaluated in.</param>
    /// <param name="caseBlock">The <c>Case</c> whose clauses are matched.</param>
    /// <param name="selector">The value of the selector, as it was evaluated: a <c>Variant</c> one still held in its <c>Variant</c>.</param>
    /// <param name="context">The scope the clauses are evaluated in.</param>
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
        CaseValueRangeClauseNode value => EvaluateComparison(session, clause, Tokens.CompareEqualOp, selector, value.Value, context),

        // "the expression <select-expression> <comparison-operator> <expression> is evaluated"
        CaseComparisonRangeClauseNode comparison => EvaluateComparison(session, clause, comparison.ComparisonOperator, selector, comparison.Value, context),

        // "the expression ((select-expression) >= (start-value)) And ((select-expression) <= (end-value)) is evaluated"
        CaseToRangeClauseNode range => EvaluateRange(session, selector, range, context),

        _ => RuntimeSemanticsEvaluationResult.InternalError(),
    };

    // The selector was already evaluated once (by EvaluateSelector); operand is the clause's own real
    // expression, evaluated fresh. The location-bearing node passed to the operator is always operand -
    // a real, source-derived node - never a stand-in fabricated for the already-known selector value.
    private RuntimeSemanticsEvaluationResult EvaluateComparison(
        IRuntimeSession session, CaseRangeClauseNode clause, string comparisonOperator, VBTypedValue selector, ExpressionNode operand, RuntimeEvaluationContext context)
    {
        if (expressionEvaluator.TryCompareUnboxed(session, comparisonOperator, selector, operand, context, out var isMatch))
        {
            return RuntimeSemanticsEvaluationResult.Success(isMatch ? VBBooleanValue.True : VBBooleanValue.False);
        }

        var operandResult = expressionEvaluator.Evaluate(session, operand, context);
        if (!operandResult.IsSuccess)
        {
            return operandResult;
        }

        var converted = ConvertToSelectorType(session, clause, comparisonOperator, selector, operand, operandResult.Result!);
        if (!converted.IsSuccess)
        {
            return converted;
        }

        return operators.EvaluateBinaryOperator(session, comparisonOperator, operand, selector, converted.Result!, ConversionSite.CaseTest);
    }

    private RuntimeSemanticsEvaluationResult EvaluateRange(IRuntimeSession session, VBTypedValue selector, CaseToRangeClauseNode range, RuntimeEvaluationContext context)
    {
        var lowerBound = EvaluateComparison(session, range, Tokens.CompareGreaterThanOrEqualOp, selector, range.Start, context);
        if (!lowerBound.IsSuccess)
        {
            return lowerBound;
        }

        var upperBound = EvaluateComparison(session, range, Tokens.CompareLessThanOrEqualOp, selector, range.End, context);
        if (!upperBound.IsSuccess)
        {
            return upperBound;
        }

        return operators.EvaluateBinaryOperator(session, Tokens.LogicalAndOp, range.Start, lowerBound.Result!, upperBound.Result!, ConversionSite.CaseTest);
    }

    // MS-VBA let-coerces the value of a clause to the declared type of the selector, and compares the two in it - where MS-VBAL states the comparison
    // as an expression of the two, of the effective type of their types (RD-VBAL 5.4.2.10; Excel, oracle s46 F15a-e, s48 G01-G20): with n a Long of 2,
    // Case 1.7 matches, and Case "abc" raises 13. A selector declared Variant is compared with the value as it is (s48 G04, G14).
    private RuntimeSemanticsEvaluationResult ConvertToSelectorType(
        IRuntimeSession session, CaseRangeClauseNode clause, string comparisonOperator, VBTypedValue selector, ExpressionNode operand, VBTypedValue value)
    {
        if (DestinationOf(selector, value) is not { } destination)
        {
            return RuntimeSemanticsEvaluationResult.Success(value);
        }

        if (observation is { IsSuspended: false })
        {
            Observe(observation, clause, comparisonOperator, destination, selector, value);
        }

        if (expressionEvaluator.TryConvertUnobserved(value, destination, out var planned))
        {
            return RuntimeSemanticsEvaluationResult.Success(planned);
        }

        var frame = new LetCoercionStackFrame(operand.Identity, InputIndex.CoercionSourceValue, value, new VBTypeDescValue(destination), ConversionSite.CaseTest);
        var coerced = letCoercion.EvaluateLetCoercionSemantics(session.Symbols.Resolver, operand, frame);
        return !coerced.IsApplicable ? RuntimeSemanticsEvaluationResult.InternalError()
            : coerced.IsSuccess ? RuntimeSemanticsEvaluationResult.Success(coerced.Result!)
            : RuntimeSemanticsEvaluationResult.Error(coerced.ErrorInfo!);
    }

    // the type the value of a clause is converted to: the declared type of the selector, when it is one of the intrinsic types MS-VBA converts to. A String
    // of a fixed length is a String (s48 H07: with f a String * 5 of "ab", Case "ab" does not match, and Case "ab   " does). An integer is not rounded to a
    // Single, which MS-VBA loads at its exact value (as the x87 does, RD-VBAL 5.6.9.3): with s a Single of 16777216, Case 16777217 does not match, and
    // Case Is < 16777217 does (s48 M02, s42 D22) - where a Double, a String and a Single are converted to the selector's type (M01, M03, M06, M07). It is
    // compared as it is, which a Single and an integer are in a Double (RD-VBAL 5.6.9.5).
    private static VBType? DestinationOf(VBTypedValue selector, VBTypedValue value)
    {
        var destination = selector switch
        {
            VBVariantValue => null,
            VBStringValue => VBStringType.TypeInfo,
            { TypeInfo: VBNumericType or VBBooleanType or VBDateType } => selector.TypeInfo,
            _ => null,
        };

        return destination is VBSingleType && HeldValueOf(value).TypeInfo is VBByteType or VBIntegerType or VBLongType or VBLongLongType
            ? null
            : destination;
    }

    private static VBTypedValue HeldValueOf(VBTypedValue value) => value is VBVariantValue { TypedValue: var held } ? HeldValueOf(held) : value;

    // a clause is stated when its value is compared in the selector's type where the expression MS-VBAL states would compare the two in another.
    private static void Observe(AnalysisObservation into, CaseRangeClauseNode clause, string comparisonOperator, VBType destination, VBTypedValue selector, VBTypedValue value)
    {
        if (OperationPlanner.ForValues(comparisonOperator, destination, value.TypeInfo).EffectiveType is { } effective && effective.Equals(destination))
        {
            return;
        }

        var form = clause switch
        {
            CaseComparisonRangeClauseNode => CaseClauseSemanticFlags.ComparisonForm,
            CaseToRangeClauseNode => CaseClauseSemanticFlags.RangeForm,
            _ => CaseClauseSemanticFlags.ValueForm,
        };

        into.OnStatement(new CaseClauseStatementFact(clause.Identity, clause.SourceLocation, Tokens.Case, form | CaseClauseSemanticFlags.ComparedInSelectorType,
            IsValueKnown: !selector.IsIndeterminate && !value.IsIndeterminate));
    }
}
