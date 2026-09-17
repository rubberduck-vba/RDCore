using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Semantics.Static.Abstract;
using RDCore.SDK.Semantics.Static.Expressions;
using RDCore.SDK.Semantics.Static.Operators;

namespace RDCore.SDK.Semantics.Static;

/// <summary>
/// Recursively determines the declared type of a real, arbitrarily-nested expression tree.
/// </summary>
/// <remarks>
/// Every individual <see cref="IStaticSemantics"/> rule (<see cref="SimpleNameExpressionStaticSemantics"/>,
/// <see cref="MemberAccessExpressionStaticSemantics"/>, operators, ...) only knows how to combine
/// already-resolved operand types for the one node it handles — nothing recurses into an expression's
/// own children first to produce them. This is that missing piece: given any <see cref="ExpressionNode"/>,
/// it dispatches by the node's own C# type (and, for operator nodes, by <c>Token</c>) to the matching
/// rule, evaluating children first and short-circuiting on the first error, so a nested expression like
/// <c>Foo.Bar.Baz</c> or <c>x + 1</c> resolves end to end instead of only ever being unit-tested with
/// hand-fed operand types.
/// </remarks>
public static class ExpressionStaticSemanticsEvaluator
{
    /// <summary>
    /// Determines the declared type of <paramref name="expression"/>, recursively evaluating its
    /// children first wherever a rule needs their declared types as operands.
    /// </summary>
    /// <param name="context">The compile-time context this expression is evaluated against.</param>
    /// <param name="expression">The expression to evaluate — may be arbitrarily nested.</param>
    /// <returns>
    /// The result of the first rule (own or a child's) that fails, or the final resolved type when the
    /// whole tree evaluates successfully. A node kind with no rule yet (or an operator token with no
    /// mapped rule, e.g. <c>Mod</c>) defers to <see cref="VBUnknownType"/> rather than erroring — the
    /// same "not modeled yet, not wrong" convention every existing rule already uses for its own gaps.
    /// </returns>
    public static StaticSemanticsEvaluationResult Evaluate(StaticEvaluationContext context, ExpressionNode expression)
        => expression switch
        {
            LiteralExpressionNode => LiteralExpressionStaticSemantics.Instance.DetermineDeclaredType(context, expression),
            SimpleNameExpressionNode => SimpleNameExpressionStaticSemantics.Instance.DetermineDeclaredType(context, expression),
            InstanceExpressionNode => InstanceExpressionStaticSemantics.Instance.DetermineDeclaredType(context, expression),
            // TypeExpression names a type, not a value - nothing to recurse into as an expression.
            NewExpressionNode => NewExpressionStaticSemantics.Instance.DetermineDeclaredType(context, expression),
            MemberAccessExpressionNode memberAccess => EvaluateMemberAccess(context, expression, memberAccess),
            VBBinaryOperatorExpressionNode binaryOperator => EvaluateBinaryOperator(context, expression, binaryOperator),
            VBUnaryOperatorExpressionNode unaryOperator => EvaluateUnaryOperator(context, expression, unaryOperator),
            _ => StaticSemanticsEvaluationResult.Success(VBUnknownType.TypeInfo),
        };

    private static StaticSemanticsEvaluationResult EvaluateMemberAccess(
        StaticEvaluationContext context, ExpressionNode expression, MemberAccessExpressionNode memberAccess)
    {
        if (memberAccess.Owner is not { } owner)
        {
            // a With-relative access (.Member) needs the enclosing With block's target type; no
            // statement-level walker exists yet to supply one here. MemberAccessExpressionStaticSemantics
            // itself throws if called this way (a null owner is caller error, not something to feed it),
            // so defer instead of violating that contract - not yet modeled, not wrong.
            return StaticSemanticsEvaluationResult.Success(VBUnknownType.TypeInfo);
        }

        var ownerResult = Evaluate(context, owner);
        if (ownerResult.IsError)
        {
            return ownerResult;
        }

        return MemberAccessExpressionStaticSemantics.Instance.DetermineDeclaredType(context, expression, ownerResult.Result!);
    }

    private static StaticSemanticsEvaluationResult EvaluateBinaryOperator(
        StaticEvaluationContext context, ExpressionNode expression, VBBinaryOperatorExpressionNode binaryOperator)
    {
        var leftResult = Evaluate(context, binaryOperator.Left);
        if (leftResult.IsError)
        {
            return leftResult;
        }
        var rightResult = Evaluate(context, binaryOperator.Right);
        if (rightResult.IsError)
        {
            return rightResult;
        }

        return ResolveBinaryOperatorRule(binaryOperator.Token) is { } rule
            ? rule.DetermineDeclaredType(context, expression, leftResult.Result!, rightResult.Result!)
            : StaticSemanticsEvaluationResult.Success(VBUnknownType.TypeInfo);
    }

    private static StaticSemanticsEvaluationResult EvaluateUnaryOperator(
        StaticEvaluationContext context, ExpressionNode expression, VBUnaryOperatorExpressionNode unaryOperator)
    {
        var operandResult = Evaluate(context, unaryOperator.Operand);
        if (operandResult.IsError)
        {
            return operandResult;
        }

        return ResolveUnaryOperatorRule(unaryOperator.Token) is { } rule
            ? rule.DetermineDeclaredType(context, expression, operandResult.Result!)
            : StaticSemanticsEvaluationResult.Success(VBUnknownType.TypeInfo);
    }

    // One rule instance per call; every operator rule is either a parameterless-constructible record
    // or a shared Lazy singleton, so this never allocates anything meaningful. `Mod` (Tokens.ModuloOp)
    // has no rule yet - falls through to null, deferred by the caller like any other unmapped case.
    private static IStaticSemantics? ResolveBinaryOperatorRule(string token) => token switch
    {
        Tokens.AdditionOp => new BinaryAdditionOperatorStaticSemantics(),
        Tokens.SubtractionOp => new BinarySubtractionOperatorStaticSemantics(),
        Tokens.MultiplicationOp => new BinaryMultiplicationOperatorStaticSemantics(),
        Tokens.DivisionOp => new BinaryDivisionOperatorStaticSemantics(),
        Tokens.IntegerDivisionOp => new BinaryIntegerDivisionOperatorStaticSematics(),
        Tokens.PowerOp => new BinaryExponentOperatorStaticSemantics(),
        Tokens.ConcatOp => new BinaryConcatOperatorStaticSemantics(),
        Tokens.CompareIsOp => new BinaryIsRefEqOperatorStaticSemantics(),
        Tokens.CompareEqualOp or Tokens.CompareNotEqualOp or Tokens.CompareGreaterThanOp
            or Tokens.CompareGreaterThanOrEqualOp or Tokens.CompareLessThanOp
            or Tokens.CompareLessThanOrEqualOp or Tokens.CompareLikeOp => new BinaryRelationalOperatorStaticSemantics(),
        Tokens.LogicalAndOp or Tokens.LogicalOrOp or Tokens.LogicalXOrOp
            or Tokens.LogicalEqvOp or Tokens.LogicalImpOp => new BinaryLogicalOperatorStaticSemantics(),
        _ => null,
    };

    private static IStaticSemantics? ResolveUnaryOperatorRule(string token) => token switch
    {
        Tokens.NegationOp => new UnaryNegationOperatorStaticSemantics(),
        Tokens.LogicalNotOp => new UnaryLogicalOperatorStaticSemantics(),
        _ => null,
    };
}
