using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.SDK.Semantics.Static.Operators;

/// <summary>
/// <strong>MS-VBAL §5.6.6 Parenthesized Expressions</strong> (static semantics), for the parentheses around an operand of an operator: the
/// <c>"__p()_op"</c> precedence operator (<strong>RD-VBAL §5.6.6</strong>), which groups the operand.
/// </summary>
/// <remarks>
/// <para>
/// "The declared type of a parenthesized expression is that of the enclosed expression", and it "evaluates to the simple data value of its enclosed
/// expression": an object in parentheses is its default member's value, even as an operand of an operator that takes an object - <c>(o) Is Nothing</c>
/// tests what <c>o</c>'s default member returns, and <c>((o))</c> reads it twice (MS-VBA, recorded in Excel). The operand is evaluated as a value
/// before this rule is asked for its declared type, which is that value's.
/// </para>
/// <para>
/// What sets the operator apart from the <c>"__c()_op"</c> let-coercion operator the same parentheses are anywhere else
/// (<see cref="UnaryLetCoerceOperatorStaticSemantics"/>) is where it is written, and so what it means to the code that reads it: these parentheses group;
/// the others make a value of what would otherwise be a variable.
/// </para>
/// </remarks>
public sealed record class UnaryPrecedenceOperatorStaticSemantics() : StaticSemantics()
{
    /// <summary>
    /// The declared type of the enclosed expression's value.
    /// </summary>
    /// <param name="context">The compile-time context the expression is evaluated against.</param>
    /// <param name="expression">The parenthesized expression.</param>
    /// <param name="operandDeclaredTypes">The declared type of the enclosed expression's value.</param>
    public override StaticSemanticsEvaluationResult DetermineDeclaredType(StaticEvaluationContext context, ExpressionNode expression, params VBType[] operandDeclaredTypes)
        => StaticSemanticsEvaluationResult.Success(operandDeclaredTypes[(int)InputIndex.UnaryOperand]);
}
