using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.SDK.Semantics.Static.Operators;

/// <summary>
/// <strong>MS-VBAL 5.6.9.3.4</strong> Binary '*' Operator (static semantics)
/// </summary>
public sealed record class BinaryMultiplicationOperatorStaticSemantics : BinaryArithmeticOperatorStaticSemantics
{
    /// <summary>
    /// Determines a static <see cref="VBType"/> from specified operands.
    /// </summary>
    /// <param name="resolver">The static context containing the available static memory space.</param>
    /// <param name="expression">The <em>expression node</em> being evaluated.</param>
    /// <param name="lhs">The declared type of the <em>left-hand side</em> (LHS) operand involved in the evaluation.</param>
    /// <param name="rhs">The declared type of the <em>right-hand side</em> (RHS) operand involved in the evaluation.</param>
    /// <returns>
    /// A <see cref="StaticSemanticsEvaluationResult"/> encapsulating the resulting <see cref="VBType"/> if successful, or <see cref="VBCompileErrorInfo"/> error metadata otherwise.
    /// </returns>
    protected override StaticSemanticsEvaluationResult DetermineOperatorStaticType(ISymbolResolver resolver, BoundExpression expression, VBType lhs, VBType rhs)
    {
        return lhs switch
        {
            VBCurrencyType 
                when rhs is VBSingleType or VBDoubleType or VBFixedStringType or VBStringType 
                    => StaticSemanticsEvaluationResult.Success(VBDoubleType.TypeInfo),
            VBSingleType or VBDoubleType or VBFixedStringType or VBStringType 
                when rhs is VBCurrencyType 
                    => StaticSemanticsEvaluationResult.Success(VBDoubleType.TypeInfo),

            VBDateType 
                when rhs is VBNumericType or VBFixedStringType or VBStringType or VBDateType 
                    => StaticSemanticsEvaluationResult.Success(VBDoubleType.TypeInfo),                
            VBNumericType or VBFixedStringType or VBStringType or VBDateType 
                when rhs is VBDateType 
                    => StaticSemanticsEvaluationResult.Success(VBDoubleType.TypeInfo),
        
            _ => base.DetermineOperatorStaticType(resolver, expression, lhs, rhs)
        };
    }
}
