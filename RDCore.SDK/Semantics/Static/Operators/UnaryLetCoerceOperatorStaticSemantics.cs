using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.SDK.Semantics.Static.Operators;

/// <summary>
/// <strong>MS-VBAL 5.6.6 Parenthesized Expressions</strong> (static semantics): the operator a pair of
/// parentheses around an expression is.
/// </summary>
/// <remarks>
/// "The declared type of a parenthesized expression is that of the enclosed expression" — so this
/// determines no type of its own, and no operand type is a mismatch. What the parentheses change is the
/// expression's <em>classification</em>: 5.6.6 classifies it as a value expression, where the enclosed
/// expression may have been a variable. That is not a declared type, and it is not recorded here — it is
/// what the operator's run-time semantics produce.
/// <para>
/// The binary form of the same operator (an explicit coercion to a named target type) is
/// <see cref="BinaryLetCoerceOperatorStaticSemantics"/>.
/// </para>
/// </remarks>
public sealed record class UnaryLetCoerceOperatorStaticSemantics() : StaticSemantics()
{
    /// <summary>
    /// Determines a static <see cref="VBType"/> from the specified operands.
    /// </summary>
    /// <param name="context">The compile-time context this expression is evaluated against.</param>
    /// <param name="expression">The <em>expression node</em> being evaluated.</param>
    /// <param name="operandDeclaredTypes">The declared type of each operand involved in the evaluation.</param>
    /// <returns>
    /// A <see cref="StaticSemanticsEvaluationResult"/> encapsulating the enclosed expression's own
    /// declared type.
    /// </returns>
    public override StaticSemanticsEvaluationResult DetermineDeclaredType(StaticEvaluationContext context, ExpressionNode expression, params VBType[] operandDeclaredTypes)
        => StaticSemanticsEvaluationResult.Success(operandDeclaredTypes[(int)InputIndex.UnaryOperand]);
}
