using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.SDK.Semantics.Static.Abstract;

/// <summary>
/// Uses pattern-matching rules to encapsulate binary relational operator static semantics as defined in <strong>MS-VBAL 5.6.9.5</strong>.
/// </summary>
public record class BinaryRelationalOperatorStaticSemantics : StaticSemantics, IStaticSemantics
{
    /// <summary>
    /// MS-VBAL 5.6.9.5 Relational Operators (static semantics) 
    /// The operator has the declared type returned by this method, based on the declared type of its operands.
    /// </summary>
    /// <param name="resolver">The static context containing the available static memory space.</param>
    /// <param name="expression">The <em>expression node</em> being evaluated.</param>
    /// <param name="operandDeclaredTypes">The declared type of the operands.</param>
    public override StaticSemanticsEvaluationResult DetermineDeclaredType(
        ISymbolResolver resolver,
        BoundExpression expression,
        params VBType[] operandDeclaredTypes)
        => DetermineOperatorStaticType(expression, operandDeclaredTypes[(int)InputIndex.BinaryLeftOperand], operandDeclaredTypes[(int)InputIndex.BinaryRightOperand]);

    /// <summary>
    /// MS-VBAL 5.6.9.5 Relational Operators (static semantics) 
    /// The operator has the declared type returned by this method, based on the declared type of its operands.
    /// </summary>
    /// <param name="expression">The <em>expression node</em> being evaluated.</param>
    /// <param name="lhs">The declared type of the LHS operand.</param>
    /// <param name="rhs">The declared type of the RHS operand.</param>
    protected virtual StaticSemanticsEvaluationResult DetermineOperatorStaticType(BoundExpression expression, VBType lhs, VBType rhs)
        => lhs switch
        {
            not VBArrayType and not VBUserDefinedType and not VBVariantType
                when rhs is not VBArrayType and not VBUserDefinedType and not VBVariantType => VBBooleanType.TypeInfo,

            not VBArrayType and not VBUserDefinedType when rhs is VBVariantType => VBVariantType.TypeInfo,
            VBVariantType when rhs is not VBArrayType and not VBUserDefinedType => VBVariantType.TypeInfo,

            _ => (VBType?)null
        } is VBType result
            ? StaticSemanticsEvaluationResult.Success(result)
            : StaticSemanticsEvaluationResult.Error(GetStaticTypeMismatchErrorInfo(expression, [lhs, rhs]));
}
