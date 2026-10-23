using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.SDK.Semantics.Static.Operators;

/// <summary>
/// <strong>MS-VBAL §5.6.6 Parenthesized Expressions</strong> (static semantics), for the parentheses around an operand of an operator: the
/// <c>"__p()_op"</c> precedence operator (<strong>RD-VBAL §5.6.6</strong>), which groups the operand and does nothing else.
/// </summary>
/// <remarks>
/// "The declared type of a parenthesized expression is that of the enclosed expression." The operator the operand is an operand of reads its value, as
/// it reads any operand's: the parentheses add no coercion of their own, which is what sets them apart from the <c>"__c()_op"</c> let-coercion operator
/// that the same parentheses are anywhere else (<see cref="UnaryLetCoerceOperatorStaticSemantics"/>).
/// </remarks>
public sealed record class UnaryPrecedenceOperatorStaticSemantics() : StaticSemantics()
{
    /// <summary>
    /// The declared type of the enclosed expression.
    /// </summary>
    /// <param name="context">The compile-time context the expression is evaluated against.</param>
    /// <param name="expression">The parenthesized expression.</param>
    /// <param name="operandDeclaredTypes">The declared type of the enclosed expression.</param>
    public override StaticSemanticsEvaluationResult DetermineDeclaredType(StaticEvaluationContext context, ExpressionNode expression, params VBType[] operandDeclaredTypes)
        => StaticSemanticsEvaluationResult.Success(operandDeclaredTypes[(int)InputIndex.UnaryOperand]);
}
