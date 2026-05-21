using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Expressions;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;

namespace RDCore.SDK.Semantics.Runtime.Abstract;

/// <summary>
/// The class at the base of the runtime semantics type hierarchy that implements all the runtime semantic rules defined in MS-VBAL.
/// </summary>
public abstract record class RuntimeSemantics() : IRuntimeSemantics
{
    /// <summary>
    /// Determines the <em>effective type</em> of an operation based on the data type of its operands.
    /// </summary>
    /// <param name="operandDeclaredTypes">An array containing the declared data types.</param>
    /// <remarks>
    /// <param name="context">The execution context and memory space to operate with.</param>
    /// For unary operators, the operand is expected at index 0. 
    /// For binary operators, LHS is read at index 0, RHS at index 1.
    /// </remarks>
    public abstract VBType? DetermineEffectiveType(IVBExecutionContext context, params VBType[] operandDeclaredTypes);

    /// <summary>
    /// Evaluates the specified <c>expression</c> in the specified execution context, using the specified operands.
    /// </summary>
    /// <param name="context">The execution context and memory space to operate with.</param>
    /// <param name="expression">The expression to be evaluated.</param>
    /// <param name="operands">The operands of the operation.</param>
    /// <returns></returns>
    public VBTypedValue? Evaluate(IVBExecutionContext context, ValuedExpression expression, params VBTypedValue[] operands)
    {
        var effectiveType = DetermineEffectiveType(context, [.. operands.Select(op => op.TypeInfo)])
            ?? throw VBRuntimeErrorException.TypeMismatch(expression.Location.Range);

        var validOperands = operands.Select(operand => 
            ValidateOperand(context, effectiveType, expression, operand));

        return EvaluateExpressionResult(context, expression, effectiveType, [.. validOperands]);
    }

    /// <summary>
    /// Evaluates a resulting <c>VBTypedValue</c> for a given <c>ValuedExpression</c>.
    /// </summary>
    /// <param name="context">An execution context and memory space to operate in.</param>
    /// <param name="expression">Any <c>ValuedExpression</c> to be evaluated.</param>
    /// <param name="effectiveType">The semantically determined <em>effective type</em> of the operation.</param>
    /// <param name="operands">An array of <c>VBTypedValue</c> containing the operands to work with.</param>
    /// <returns>
    /// Returns <c>null</c> if no result could be determined, if no runtime exceptions were thrown.
    /// </returns>
    protected abstract VBTypedValue? EvaluateExpressionResult(IVBExecutionContext context, ValuedExpression expression, VBType effectiveType, VBTypedValue[] operands);

    private static VBTypedValue ValidateOperand(IVBExecutionContext context, VBType effectiveType, ValuedExpression expression, VBTypedValue operand) =>
        operand is not VBNullValue
            ? LetCoerceNonNullOperand(context, expression, effectiveType, operand)
                ?? throw VBRuntimeErrorException.TypeMismatch(expression.Location.Range)
            : operand;

    private static VBTypedValue? LetCoerceNonNullOperand(IVBExecutionContext context, ValuedExpression expression, VBType effectiveType, VBTypedValue operand)
    {
        VBTypedValue? letCoercedOperand = effectiveType.Equals(operand.TypeInfo) ? operand : null;
        if (letCoercedOperand is not null)
        {
            // if the effective type is the same, we're done here.
            return letCoercedOperand;
        }

        return LetCoercionRuntimeSemantics.Instance
            .EvaluateExpressionResult(context, expression, effectiveType, [operand]) is VBTypedValue result ? result : operand;
    }
}
