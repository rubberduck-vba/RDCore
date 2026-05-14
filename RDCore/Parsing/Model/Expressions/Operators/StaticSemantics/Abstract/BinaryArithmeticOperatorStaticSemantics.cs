using RDCore.Parsing.Model.Types;
using RDCore.Parsing.Model.Types.Complex;

namespace RDCore.Parsing.Model.Expressions.Operators.StaticSemantics.Abstract;
internal abstract record class BinaryArithmeticOperatorStaticSemantics : ArithmeticOperatorStaticSemantics
{
    public sealed override VBType? DetermineDeclaredType(params VBType[] operandDeclaredTypes)
        => DetermineOperatorStaticType(operandDeclaredTypes[0], operandDeclaredTypes[1]);

    /// <summary>
    /// MS-VBAL 5.6.9.3 Arithmetic Operators (static semantics) 
    /// The operator has the declared type returned by this method, based on the declared type of its operands.
    /// </summary>
    /// <param name="lhs">The declared type of the LHS operand.</param>
    /// <param name="rhs">The declared type of the RHS operand.</param>
    /// <returns><c>null</c> if no type is statically valid.</returns>
    protected virtual VBType? DetermineOperatorStaticType(VBType lhs, VBType rhs)
    {
        return lhs switch
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

            VBDoubleType or VBStringType or VBFixedStringType when rhs is IIntegralNumericType or IFloatingPointNumericType or VBStringType or VBFixedStringType => VBDoubleType.TypeInfo,
            IIntegralNumericType or IFloatingPointNumericType or VBStringType or VBFixedStringType when rhs is VBDoubleType or VBStringType or VBFixedStringType => VBDoubleType.TypeInfo,

            VBCurrencyType when rhs is INumericType or VBStringType or VBFixedStringType => VBCurrencyType.TypeInfo,
            INumericType or VBStringType or VBFixedStringType when rhs is (VBCurrencyType) => VBCurrencyType.TypeInfo,

            VBDateType when rhs is INumericType or VBStringType or VBFixedStringType or VBDateType => VBDateType.TypeInfo,
            (INumericType or VBStringType or VBFixedStringType or VBDateType) when rhs is VBDateType => VBDateType.TypeInfo,

            VBVariantType when rhs is not (VBArrayType or VBUserDefinedType) => VBVariantType.TypeInfo,
            not (VBArrayType or VBUserDefinedType) when rhs is VBVariantType => VBVariantType.TypeInfo,

            _ => default
        };
    }
}
