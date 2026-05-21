using RDCore.Runtime.Semantics.Abstract;
using RDCore.SDK.Model.Expressions;
using RDCore.SDK.Model.Expressions.Operators;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;

namespace RDCore.Runtime.Semantics.Operators;

public abstract record class BinaryOperatorRuntimeSemantics() : RuntimeSemantics()
{
    public sealed override VBType? DetermineEffectiveType(params VBType[] operandDeclaredTypes)
        => DetermineOperatorEffectiveType(operandDeclaredTypes[0], operandDeclaredTypes[1]);

    protected sealed override VBTypedValue? EvaluateExpressionResult(IVBExecutionContext context, ValuedExpression expression, VBType effectiveType, VBTypedValue[] operands) 
        => EvaluateExpressionResult(context, (VBBinaryOperatorExpression)expression, effectiveType, operands[0], operands[1]);

    /// <summary>
    /// Gets a (managed/.NET) <c>double</c> value if the value is a <c>VBNumericTypedValue</c>,
    /// or uses <c>INumericCoercion.AsCoercedDouble</c> (recursive) to retrieve one if possible.
    /// </summary>
    /// <returns>
    /// <c>null</c> if no <c>double</c> value could be extracted out of the specified typed value.
    /// </returns>
    protected static double? CoerceAndUnwrapNumericValue(VBTypedValue value)
    {
        if (value is VBNullValue)
        {
            return null;
        }

        var depth = 0;
        return value is VBNumericTypedValue numValue
            ? numValue.ManagedValue
            : value is INumericCoercion coercibleValue
                ? coercibleValue.AsCoercedDouble(ref depth)?.ManagedValue
                : null;
    }

    /// <summary>
    /// Gets a (managed/.NET) <c>string</c> value if the value is a <c>VBStringValue</c> (or <c>VBFixedStringValue</c>),
    /// or uses <c>IStringCoercion.AsCoercedString</c> (recursive) to retrieve one if possible.
    /// </summary>
    /// <returns>
    /// <c>null</c> if no <c>string</c> value could be extracted out of the specified typed value.
    /// </returns>
    protected string? CoerceAndUnwrapStringValue(VBTypedValue value)
    {
        var depth = 0;
        return value is VBStringValue strValue
            ? strValue.Value
            : value is IStringCoercion coercibleValue
                ? coercibleValue.AsCoercedString(ref depth)?.Value
                : null;
    }

    protected virtual VBTypedValue? EvaluateExpressionResult(IVBExecutionContext context, VBBinaryOperatorExpression expression, VBType effectiveType, VBTypedValue lhs, VBTypedValue rhs)
    {
        return default;
    }

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

            VBLongLongType when rhs is IIntegralNumericType or VBEmptyType => VBLongLongType.TypeInfo,
            IIntegralNumericType or VBEmptyType when rhs is VBLongLongType => VBLongLongType.TypeInfo,

            VBSingleType when rhs is VBByteType or VBBooleanType or VBIntegerType or VBSingleType or VBEmptyType => VBSingleType.TypeInfo,
            VBByteType or VBBooleanType or VBIntegerType or VBSingleType or VBEmptyType when rhs is VBSingleType => VBSingleType.TypeInfo,

            VBSingleType when rhs is VBLongType or VBLongLongType => VBDoubleType.TypeInfo,
            VBLongType or VBLongLongType when rhs is VBSingleType => VBDoubleType.TypeInfo,
            VBDoubleType or VBStringType when rhs is IIntegralNumericType or IFloatingPointNumericType or VBStringType or VBEmptyType => VBDoubleType.TypeInfo,
            IIntegralNumericType or IFloatingPointNumericType or VBStringType or VBEmptyType when rhs is VBDoubleType or VBStringType => VBDoubleType.TypeInfo,

            VBCurrencyType when rhs is IIntegralNumericType or IFloatingPointNumericType or VBCurrencyType or VBStringType or VBEmptyType => VBCurrencyType.TypeInfo,
            IIntegralNumericType or IFloatingPointNumericType or VBStringType or VBEmptyType when rhs is VBCurrencyType => VBCurrencyType.TypeInfo,

            // date values are let-coerced to VBDoubleValue
            VBDateType when rhs is IIntegralNumericType or IFloatingPointNumericType or VBStringType or VBDateType or VBEmptyType => VBDateType.TypeInfo,
            IIntegralNumericType or IFloatingPointNumericType or VBStringType or VBDateType or VBEmptyType when rhs is VBDateType => VBDateType.TypeInfo,

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
