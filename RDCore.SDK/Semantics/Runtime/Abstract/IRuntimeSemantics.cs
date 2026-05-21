using RDCore.SDK.Model.Expressions;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Runtime;

namespace RDCore.SDK.Semantics.Runtime.Abstract;

/// <summary>
/// Represents any runtime semantics rules.
/// </summary>
public interface IRuntimeSemantics
{
    /// <summary>
    /// Evaluates the specified <c>expression</c> in the specified execution context, using the specified operands.
    /// </summary>
    /// <param name="context">The execution context and memory space to operate with.</param>
    /// <param name="expression">The expression to be evaluated.</param>
    /// <param name="operands">The operands of the operation.</param>
    /// <returns></returns>
    VBTypedValue? Evaluate(IVBExecutionContext context, ValuedExpression expression, params VBTypedValue[] operands);
    /// <summary>
    /// Determines the <em>effective type</em> of an operation based on the data type of its operands.
    /// </summary>
    /// <param name="operandDeclaredTypes">An array containing the declared data types.</param>
    /// <remarks>
    /// <param name="context">The execution context and memory space to operate with.</param>
    /// For unary operators, the operand is expected at index 0. 
    /// For binary operators, LHS is read at index 0, RHS at index 1.
    /// </remarks>
    VBType? DetermineEffectiveType(IVBExecutionContext context, params VBType[] operandDeclaredTypes);
}