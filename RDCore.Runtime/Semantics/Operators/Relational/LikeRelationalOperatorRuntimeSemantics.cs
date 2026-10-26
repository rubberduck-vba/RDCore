using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Context;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Runtime.Semantics.Operators.Relational;

/// <summary>
/// MS-VBAL 5.6.9.6 Binary 'Like' Operator
/// </summary>
public sealed record class LikeRelationalOperatorRuntimeSemantics(
    ILetCoercionRuntimeSemanticsProvider LetCoercionSemanticsProvider,
    IVerboseMessageBuilder FormatterService)
    : BinaryRelationalOperatorRuntimeSemantics(LetCoercionSemanticsProvider, FormatterService)
{
    /// <remarks>
    /// MS-VBAL 5.6.9.6: if either operand's value is Null, the result is Null; otherwise both operands are Let-coerced to String regardless of their own value
    /// type — unlike the other relational operators, Like has no table of numeric and date effective types (<see cref="RDCore.SDK.Semantics.Plans.OperationPlanner"/>).
    /// </remarks>
    protected override string Operator => Tokens.CompareLikeOp;

    // MS-VBAL 5.6.9.6: Like let-coerces both operands to String, whatever a Variant holds.
    protected override bool RanksVariantNumberBelowString => false;

    // and matches a pattern, which compares no values.
    protected override bool ComparesValues => false;

    protected override RuntimeSemanticsEvaluationResult EvaluateBinaryOperatorExpressionResult(
        ISymbolResolver resolver,
        BinaryOperatorSemanticContext<ComparisonOperatorSemanticFlags> context,
        ExpressionNode expression,
        OperatorEvaluationFrame frame)
    {
        try
        {
            return base.EvaluateBinaryOperatorExpressionResult(resolver, context, expression, frame);
        }
        catch (InvalidLikePatternException)
        {
            // MS-VBAL 5.6.9.6: the pattern doesn't form a valid, complete like-pattern-element - run-time error 93 (Invalid pattern string),
            // which MS-VBA raises where the match reaches that element (LikePattern).
            return RuntimeSemanticsEvaluationResult.Error(OnRuntimeError(VBRuntimeErrorId.InvalidPatternString, expression,
                Exceptions.LikeOperatorRuntimeErrorExceptionInvalidPatternString_Verbose));
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Like is a match of characters against a pattern of characters, so it reads the characters of each operand: a trailing byte is
    /// not one, and is not seen.
    /// <para>
    /// 🚧 Whether MS-VBA's <c>Like</c> sees a trailing byte is yet to be run against MS-VBA.
    /// </para>
    /// </remarks>
    protected override bool ComparisonOp(VBStringValue lhs, VBStringValue rhs, StringComparisonRules rules)
        => LikePattern.Get(rhs.Value).IsMatch(lhs.Value, rules);

    protected override bool ComparisonOp<T>(T lhs, T rhs) => throw new NotSupportedException();
}
