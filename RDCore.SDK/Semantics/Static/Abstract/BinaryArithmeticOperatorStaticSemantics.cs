using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.SDK.Semantics.Static.Abstract;

/// <summary>
/// Uses pattern-matching rules to encapsulate binary arithmetic operator static semantics as defined in <strong>MS-VBAL 5.6.9.3</strong>.
/// </summary>
public abstract record class BinaryArithmeticOperatorStaticSemantics() : StaticSemantics, IStaticSemantics
{
    /// <summary>
    /// Determines a static <c>VBType</c> from specified operands.
    /// </summary>
    /// <param name="resolver">The static context containing the available static memory space.</param>
    /// <param name="expression">The <em>expression node</em> being evaluated.</param>
    /// <param name="operandDeclaredTypes">The declared type of each operand involved in the evaluation.</param>
    public override StaticSemanticsEvaluationResult DetermineDeclaredType(ISymbolResolver resolver, BoundExpression expression, params VBType[] operandDeclaredTypes)
        => DetermineOperatorStaticType(resolver, expression, operandDeclaredTypes[0], operandDeclaredTypes[1]);

    /// <summary>
    /// MS-VBAL 5.6.9.3 Arithmetic Operators (static semantics) 
    /// The operator has the declared type returned by this method, based on the declared type of its operands.
    /// </summary>
    /// <param name="resolver">The static context containing the available static memory space.</param>
    /// <param name="expression">The <em>expression node</em> being evaluated.</param>
    /// <param name="lhs">The declared type of the LHS operand.</param>
    /// <param name="rhs">The declared type of the RHS operand.</param>
    protected virtual StaticSemanticsEvaluationResult DetermineOperatorStaticType(ISymbolResolver resolver, BoundExpression expression, VBType lhs, VBType rhs)
        => lhs switch
        {
            VBByteType when rhs is VBByteType => VBByteType.TypeInfo,

            VBBooleanType or VBIntegerType when rhs is VBByteType or VBBooleanType or VBIntegerType => VBIntegerType.TypeInfo,
            VBByteType or VBBooleanType or VBIntegerType when rhs is VBBooleanType or VBIntegerType => VBIntegerType.TypeInfo,

            VBLongType when rhs is VBByteType or VBBooleanType or VBIntegerType or VBLongType => VBLongType.TypeInfo,
            VBByteType or VBBooleanType or VBIntegerType or VBLongType when rhs is VBLongType => VBLongType.TypeInfo,

            // NOTE: VBBoolean is not present in the VBLongLong MS-VBAL specifications,
            // but the behavior is observable (and consistent with the rest of the mappings) in MS-VBA (runtime semantics).
            VBLongLongType when rhs is IIntegralNumericType or VBBooleanType => VBLongLongType.TypeInfo,
            IIntegralNumericType or VBBooleanType when rhs is VBLongLongType => VBLongLongType.TypeInfo,

            VBSingleType when rhs is VBByteType or VBBooleanType or VBIntegerType => VBSingleType.TypeInfo,
            VBByteType or VBBooleanType or VBIntegerType when rhs is VBSingleType => VBSingleType.TypeInfo,

            VBSingleType when rhs is VBLongType or VBLongLongType => VBDoubleType.TypeInfo,
            VBLongType or VBLongLongType when rhs is VBSingleType => VBDoubleType.TypeInfo,

            VBDoubleType or VBFixedStringType or VBStringType when rhs is IIntegralNumericType or IFloatingPointNumericType or VBFixedStringType or VBStringType => VBDoubleType.TypeInfo,
            IIntegralNumericType or IFloatingPointNumericType or VBFixedStringType or VBStringType when rhs is VBDoubleType or VBFixedStringType or VBStringType => VBDoubleType.TypeInfo,

            VBCurrencyType when rhs is VBNumericType or VBFixedStringType or VBStringType => VBCurrencyType.TypeInfo,
            VBNumericType or VBFixedStringType or VBStringType when rhs is (VBCurrencyType) => VBCurrencyType.TypeInfo,

            VBDateType when rhs is VBNumericType or VBFixedStringType or VBStringType or VBDateType => VBDateType.TypeInfo,
            VBNumericType or VBFixedStringType or VBStringType or VBDateType when rhs is VBDateType => VBDateType.TypeInfo,

            VBVariantType when rhs is not (VBArrayType or VBUserDefinedType) => VBVariantType.TypeInfo,
            not (VBArrayType or VBUserDefinedType) when rhs is VBVariantType => VBVariantType.TypeInfo,

            _ => (VBType?)null
        } is VBType result
            ? StaticSemanticsEvaluationResult.Success(result)
            : StaticSemanticsEvaluationResult.Error(GetStaticTypeMismatchErrorInfo(expression, [lhs, rhs]));
}
