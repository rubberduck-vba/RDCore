using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.SDK.Semantics.Static.Operators;

/// <summary>
/// <strong>MS-VBAL 5.6.9.3.7</strong> Binary '^' Operator (static semantics)
/// </summary>
public sealed record class BinaryExponentOperatorStaticSemantics : BinaryArithmeticOperatorStaticSemantics
{
    /// <summary>
    /// Determines a static <see cref="VBType"/> from specified operands.
    /// </summary>
    /// <param name="context">The compile-time context this expression is evaluated against.</param>
    /// <param name="expression">The <em>expression node</em> being evaluated.</param>
    /// <param name="lhs">The declared type of the <em>left-hand side</em> (LHS) operand involved in the evaluation.</param>
    /// <param name="rhs">The declared type of the <em>right-hand side</em> (RHS) operand involved in the evaluation.</param>
    /// <returns>
    /// A <see cref="StaticSemanticsEvaluationResult"/> encapsulating the resulting <see cref="VBType"/> if successful, or <see cref="VBCompileErrorInfo"/> error metadata otherwise.
    /// </returns>
    protected override StaticSemanticsEvaluationResult DetermineOperatorStaticType(StaticEvaluationContext context, ExpressionNode expression, VBType lhs, VBType rhs) 
        => lhs switch
        {
            VBNumericType or VBFixedStringType or VBStringType or VBDateType
                when rhs is VBNumericType or VBFixedStringType or VBStringType or VBDateType
                => StaticSemanticsEvaluationResult.Success(VBDoubleType.TypeInfo),

            _ => base.DetermineOperatorStaticType(context, expression, lhs, rhs)
        };
}
