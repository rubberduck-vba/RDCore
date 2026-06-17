using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.SDK.Semantics.Static.Abstract;

/// <summary>
/// Uses pattern-matching rules to encapsulate binary logical operator static semantics as defined in <strong>MS-VBAL 5.6.9.8</strong>.
/// </summary>
public record class BinaryLogicalOperatorStaticSemantics : StaticSemantics, IStaticSemantics
{
    /// <summary>
    /// MS-VBAL 5.6.9.3 Arithmetic Operators (static semantics) 
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
    /// MS-VBAL 5.6.9.3 Arithmetic Operators (static semantics) 
    /// The operator has the declared type returned by this method, based on the declared type of its operands.
    /// </summary>
    /// <param name="expression">The <em>expression node</em> being evaluated.</param>
    /// <param name="lhs">The declared type of the LHS operand.</param>
    /// <param name="rhs">The declared type of the RHS operand.</param>
    protected virtual StaticSemanticsEvaluationResult DetermineOperatorStaticType(BoundExpression expression, VBType lhs, VBType rhs)
        => lhs switch
        {
            VBByteType when rhs is VBByteType => VBByteType.TypeInfo,
            VBBooleanType when rhs is VBBooleanType => VBBooleanType.TypeInfo,
            VBByteType or VBIntegerType when rhs is VBBooleanType or VBIntegerType => VBIntegerType.TypeInfo,
            VBBooleanType or VBIntegerType when rhs is VBByteType or VBIntegerType => VBIntegerType.TypeInfo,

            IFloatingPointNumericType or IFixedPointNumericType or VBLongType or VBFixedStringType or VBStringType or VBDateType 
                when rhs is (VBNumericType and not VBLongLongType) or VBFixedStringType or VBStringType or VBDateType => VBLongType.TypeInfo,
            (VBNumericType and not VBLongLongType) or VBFixedStringType or VBStringType or VBDateType
                when rhs is IFloatingPointNumericType or IFixedPointNumericType or VBLongType or VBFixedStringType or VBStringType or VBDateType => VBLongType.TypeInfo,
        
            VBLongLongType when rhs is VBNumericType or VBFixedStringType or VBStringType or VBDateType => VBLongLongType.TypeInfo,
            VBNumericType or VBFixedStringType or VBStringType or VBDateType when rhs is VBLongLongType => VBLongLongType.TypeInfo,

            // NOTE: MS-VBAL is actually missing the LHS Variant case, which is present in 5.6.9.3 arithmetic operators;
            // this asymetry is not justifiable otherwise than by considering it an unintended omission.
            VBVariantType when rhs is not (VBArrayType or VBUserDefinedType) => VBVariantType.TypeInfo,
            not (VBArrayType or VBUserDefinedType) when rhs is VBVariantType => VBVariantType.TypeInfo,

            _ => (VBType?)null
        } is VBType result
            ? StaticSemanticsEvaluationResult.Success(result)
            : StaticSemanticsEvaluationResult.Error(GetStaticTypeMismatchErrorInfo(expression, [lhs, rhs]));
}
