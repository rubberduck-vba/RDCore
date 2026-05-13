using RDCore.Parsing.Model.Types;

namespace RDCore.Runtime.Model.Operators.RuntimeSemantics;

internal abstract record class BinaryOperatorRuntimeSemantics : ArithmeticOperatorRuntimeSemantics
{
    public sealed override VBType? DetermineEffectiveType(params VBType[] operandDeclaredTypes)
        => DetermineOperatorEffectiveType(operandDeclaredTypes[0], operandDeclaredTypes[1]);

    /// <summary>
    /// MS-VBAL 5.6.9.3 Arithmetic Operators (runtime semantics) 
    /// The operator has the declared type returned by this method, based on the declared type of its operands.
    /// </summary>
    /// <param name="lhs">The declared type of the operand on the left side of the operator.</param>
    /// <param name="rhs">The declared type of the operand on the right side of the operator.</param>
    /// <returns><c>null</c> if no type is statically valid.</returns>
    protected virtual VBType? DetermineOperatorEffectiveType(VBType lhs, VBType rhs)
    {
        return lhs switch
        {
            VBByteType when rhs is VBByteType or VBEmptyType => VBByteType.TypeInfo,
            VBByteType or VBEmptyType when rhs is VBByteType => VBByteType.TypeInfo,

            VBBooleanType or VBIntegerType when rhs is VBByteType or VBBooleanType or VBIntegerType or VBEmptyType => VBIntegerType.TypeInfo,
            VBByteType or VBBooleanType or VBIntegerType or VBEmptyType when rhs is VBBooleanType or VBIntegerType => VBIntegerType.TypeInfo,
            VBEmptyType when rhs is VBEmptyType => VBIntegerType.TypeInfo,

            VBLongType when rhs is VBByteType or VBBooleanType or VBIntegerType or VBLongType or VBEmptyType => VBLongType.TypeInfo,
            VBByteType or VBBooleanType or VBIntegerType or VBLongType or VBEmptyType when rhs is VBLongType => VBLongType.TypeInfo,

            VBLongLongType when rhs is VBByteType or VBBooleanType or VBIntegerType or VBLongType or VBLongLongType or VBEmptyType => VBLongLongType.TypeInfo,
            VBByteType or VBBooleanType or VBIntegerType or VBLongType or VBLongLongType or VBEmptyType when rhs is VBLongLongType => VBLongLongType.TypeInfo,

            VBSingleType when rhs is VBByteType or VBBooleanType or VBIntegerType or VBSingleType or VBEmptyType => VBSingleType.TypeInfo,
            VBByteType or VBBooleanType or VBIntegerType or VBSingleType or VBEmptyType when rhs is VBSingleType => VBSingleType.TypeInfo,

            VBSingleType when rhs is VBLongType or VBLongLongType => VBDoubleType.TypeInfo,
            VBLongType or VBLongLongType when rhs is VBSingleType => VBDoubleType.TypeInfo,
            VBDoubleType or VBStringType when rhs is INumericType or VBStringType or VBEmptyType => VBDoubleType.TypeInfo,
            INumericType or VBStringType or VBEmptyType when rhs is VBDoubleType or VBStringType => VBDoubleType.TypeInfo,

            VBCurrencyType when rhs is INumericType or VBCurrencyType or VBStringType or VBEmptyType => VBCurrencyType.TypeInfo,
            INumericType or VBStringType or VBEmptyType when rhs is VBCurrencyType => VBCurrencyType.TypeInfo,

            // date values are let-coerced to VBDoubleValue
            VBDateType when rhs is INumericType or VBStringType or VBDateType or VBEmptyType => VBDateType.TypeInfo,
            INumericType or VBStringType or VBDateType or VBEmptyType when rhs is VBDateType => VBDateType.TypeInfo,

            VBDecimalType when rhs is INumericType or VBCurrencyType or VBStringType or VBEmptyType => VBCurrencyType.TypeInfo,
            INumericType or VBStringType or VBEmptyType when rhs is VBCurrencyType => VBCurrencyType.TypeInfo,

            VBNullType when rhs is INumericType or VBStringType or VBDateType or VBEmptyType or VBNullType => VBNullType.TypeInfo,
            INumericType or VBStringType or VBDateType or VBEmptyType or VBNullType when rhs is VBNullType => VBNullType.TypeInfo,

            VBErrorType when rhs is INumericType or VBStringType or VBDateType or VBEmptyType or VBErrorType => VBErrorType.TypeInfo,
            INumericType or VBStringType or VBDateType or VBEmptyType or VBErrorType when rhs is VBErrorType => VBErrorType.TypeInfo,

            _ => default
        };
    }
}
