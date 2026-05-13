using RDCore.Parsing.Model.Types;
using RDCore.Parsing.Model.Types.Complex;

namespace RDCore.Parsing.Model.Expressions.Operators.StaticSemantics.StaticSemantics;

/// <summary>
/// TODO derive static semantics for each operator; they only need to specify their respective exceptions, reading plainly like MS-VBAL.
/// </summary>
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
            VBByteType or VBBooleanType or VBIntegerType when lhs is VBBooleanType or VBIntegerType => VBIntegerType.TypeInfo,

            VBLongType when rhs is VBByteType or VBBooleanType or VBIntegerType or VBLongType => VBLongType.TypeInfo,
            VBByteType or VBBooleanType or VBIntegerType or VBLongType when rhs is VBLongType => VBLongType.TypeInfo,

            VBLongLongType when rhs is VBByteType or VBBooleanType or VBIntegerType or VBLongType or VBLongLongType => VBLongLongType.TypeInfo,
            VBByteType or VBBooleanType or VBIntegerType or VBLongType or VBLongLongType when rhs is VBLongLongType => VBLongLongType.TypeInfo,

            VBSingleType when rhs is VBByteType or VBBooleanType or VBIntegerType or VBLongType => VBSingleType.TypeInfo,
            VBByteType or VBBooleanType or VBIntegerType or VBLongType when rhs is VBSingleType => VBSingleType.TypeInfo,

            VBSingleType when rhs is VBLongType or VBLongLongType => VBDoubleType.TypeInfo,
            VBLongType or VBLongLongType when rhs is VBSingleType => VBDoubleType.TypeInfo,

            VBDoubleType or VBStringType or VBFixedStringType when rhs is INumericType or VBStringType or VBFixedStringType => VBDoubleType.TypeInfo,
            INumericType or VBStringType or VBFixedStringType when rhs is VBDoubleType or VBStringType or VBFixedStringType => VBDoubleType.TypeInfo,

            VBCurrencyType when rhs is INumericType or VBStringType or VBFixedStringType => VBCurrencyType.TypeInfo,
            INumericType or VBStringType or VBFixedStringType when lhs is (VBCurrencyType) => VBCurrencyType.TypeInfo,

            VBDateType when rhs is INumericType or VBStringType or VBFixedStringType or VBDateType => VBDateType.TypeInfo,
            INumericType or VBStringType or VBFixedStringType or VBDateType when lhs is (VBDateType) => VBDateType.TypeInfo,

            VBVariantType when rhs is not (VBArrayType or VBUserDefinedType) => VBVariantType.TypeInfo,
            not (VBArrayType or VBUserDefinedType) when rhs is VBVariantType => VBVariantType.TypeInfo,

            _ => default
        };
    }
}
