using RDCore.Parsing.Model.Types;
using RDCore.Parsing.Model.Values;
using RDCore.Server;

namespace RDCore.Runtime.Model.Operators.RuntimeSemantics;

internal abstract record class BinaryBitwiseOperator : BinaryOperatorRuntimeSemantics
{
    protected override VBTypedValue? EvaluateOperationResult(VBExecutionContext context, VBBinaryOperatorExpression expression, VBType effectiveType, VBTypedValue lhs, VBTypedValue rhs)
    {
        if (lhs is IIntegralNumericType && rhs is IIntegralNumericType)
        {
            context.AddDiagnostic(RDCoreDiagnostic.BitwiseOperator(expression.Location.Range));
            if (CoerceAndUnwrapNumericValue(lhs) is double lhsValue && CoerceAndUnwrapNumericValue(rhs) is double rhsValue)
            {
                var bitwiseResult = EvaluateBitwise(Convert.ToInt32(lhs), Convert.ToInt32(rhs));
                return new VBIntegerValue(expression.Symbol).WithValue(bitwiseResult);
            }
        }

        if (lhs is VBNullValue && rhs is VBNullValue)
        {
            return VBNullValue.Null;
        }

        return EvaluateSemanticallly(context, expression, effectiveType, lhs, rhs);
    }

    /// <summary>
    /// Evaluates the bitwise evaluation branch of the MS-VBAL specifications for a logical operator.
    /// </summary>
    /// <remarks>
    /// Operands are <strong>explicitly specified</strong> as being evaluated bitwise only given specific operands.
    /// </remarks>
    protected abstract int EvaluateBitwise(int lhs, int rhs);
    /// <summary>
    /// Evaluates the not-bitwise evaluation branches of the MS-VBAL specifications for a logical operator.
    /// </summary>
    /// <remarks>
    /// Operands are <strong>explicitly specified</strong> as being evaluated bitwise only given specific operands.
    /// Base implementation has already handled the case where both operands are integers, and the case where they're both <c>Null</c>.
    /// </remarks>
    protected abstract VBTypedValue? EvaluateSemanticallly(VBExecutionContext context, VBBinaryOperatorExpression expression, VBType effectiveType, VBTypedValue lhs, VBTypedValue rhs);
}

/// <summary>
/// MS-VBAL 5.6.9.8.2 Binary 'And' Operator
/// </summary>
internal record class BinaryAndBitwiseOperator : BinaryBitwiseOperator
{
    protected override int EvaluateBitwise(int lhs, int rhs) => lhs & rhs;

    protected override VBTypedValue? EvaluateSemanticallly(VBExecutionContext context, VBBinaryOperatorExpression expression, VBType effectiveType, VBTypedValue lhs, VBTypedValue rhs)
    {
        if (lhs is VBNumericTypedValue lhsNumeric && rhs is VBNullValue)
        {
            if (lhsNumeric.NumericValue == 0)
            {
                return VBIntegerValue.Zero;
            }
            else
            {
                return VBNullValue.Null;
            }
        }
        
        if (rhs is VBNumericTypedValue rhsNumeric && lhs is VBNullValue)
        {
            if (rhsNumeric.NumericValue == 0)
            {
                return VBIntegerValue.Zero;
            }
            else
            {
                return VBNullValue.Null;
            }
        }

        return default;
    }
}

/// <summary>
/// MS-VBAL 5.6.9.8.3 Binary 'Or' Operator
/// </summary>
internal record class BinaryOrBitwiseOperator : BinaryBitwiseOperator
{
    protected override int EvaluateBitwise(int lhs, int rhs) => lhs | rhs;

    protected override VBTypedValue? EvaluateSemanticallly(VBExecutionContext context, VBBinaryOperatorExpression expression, VBType effectiveType, VBTypedValue lhs, VBTypedValue rhs)
    {
        return lhs switch
        {
            VBTypedValue when lhs.TypeInfo is IIntegralNumericType && rhs is VBNullValue => lhs,
            VBNullValue when rhs.TypeInfo is IIntegralNumericType => rhs,
            _ => default
        };
    }
}

/// <summary>
/// MS-VBAL 5.6.9.8.4 Binary 'Xor' Operator
/// </summary>
internal record class BinaryXorBitwiseOperator : BinaryBitwiseOperator
{
    protected override int EvaluateBitwise(int lhs, int rhs)
    {
        unchecked // TODO verify if this should be allowed to overflow (edge case)
        {
            return Convert.ToInt32((long)lhs ^ (long)rhs);
        }
    }

    protected override VBTypedValue? EvaluateSemanticallly(VBExecutionContext context, VBBinaryOperatorExpression expression, VBType effectiveType, VBTypedValue lhs, VBTypedValue rhs)
    {
        return lhs switch
        {
            VBTypedValue when lhs.TypeInfo is IIntegralNumericType && rhs is VBNullValue => VBNullValue.Null,
            VBNullValue when rhs.TypeInfo is IIntegralNumericType => VBNullValue.Null,
            _ => default
        };
    }
}

/// <summary>
/// MS-VBAL 5.6.9.8.5 Binary 'Eqv' Operator
/// </summary>
internal record class BinaryEqvBitwiseOperator : BinaryBitwiseOperator
{
    protected override int EvaluateBitwise(int lhs, int rhs)
    {
        unchecked // TODO verify if this should be allowed to overflow (edge case)
        {
            return Convert.ToInt32(~((long)lhs ^ (long)rhs));
        }
    }

    protected override VBTypedValue? EvaluateSemanticallly(VBExecutionContext context, VBBinaryOperatorExpression expression, VBType effectiveType, VBTypedValue lhs, VBTypedValue rhs)
    {
        return lhs switch
        {
            VBTypedValue when lhs.TypeInfo is IIntegralNumericType && rhs is VBNullValue => VBNullValue.Null,
            VBNullValue when rhs.TypeInfo is IIntegralNumericType => VBNullValue.Null,
            _ => default
        };
    }
}

/// <summary>
/// MS-VBAL 5.6.9.8.6 Binary 'Imp' Operator
/// </summary>
internal record class BinaryImpBitwiseOperator : BinaryBitwiseOperator
{
    protected override int EvaluateBitwise(int lhs, int rhs)
    {
        return Convert.ToInt32(~(long)lhs | ~(long)rhs);
    }

    protected override VBTypedValue? EvaluateSemanticallly(VBExecutionContext context, VBBinaryOperatorExpression expression, VBType effectiveType, VBTypedValue lhs, VBTypedValue rhs)
    {
        // TODO refactor this so we can issue diagnostics for bitwise evaluations
        return lhs switch
        {
            VBNumericTypedValue lhsNumeric when lhsNumeric.NumericValue == -1 && rhs is VBNullValue 
                => VBNullValue.Null,

            VBNumericTypedValue lhsNumeric when lhs.TypeInfo is IIntegralNumericType && lhsNumeric.NumericValue != -1 && rhs is VBNullValue 
                => (VBTypedValue)effectiveType.CreateNumericValue(expression.Symbol).WithValue(EvaluateBitwise((int)lhsNumeric.NumericValue, 0)),

            VBNullValue when rhs.TypeInfo is IIntegralNumericType && rhs is VBNumericTypedValue rhsNumeric && rhsNumeric.NumericValue != 0 
                => rhs,

            VBNullValue when rhs is VBNumericTypedValue rhsNumeric && rhsNumeric.NumericValue == 0 
                => VBNullValue.Null,

            _ => default
        };
    }
}

internal abstract record class BinaryOperatorRuntimeSemantics : RuntimeSemantics
{
    public sealed override VBType? DetermineEffectiveType(params VBType[] operandDeclaredTypes)
        => DetermineOperatorEffectiveType(operandDeclaredTypes[0], operandDeclaredTypes[1]);

    protected sealed override VBTypedValue? EvaluateOperationResult(VBExecutionContext context, VBOperatorExpression expression, VBType effectiveType, VBTypedValue[] operands) 
        => EvaluateOperationResult(context, (VBBinaryOperatorExpression)expression, effectiveType, operands[0], operands[1]);

    /// <summary>
    /// Gets a (managed/.NET) <c>double</c> value if the value is a <c>VBNumericTypedValue</c>,
    /// or uses <c>INumericCoercion.AsCoercedDouble</c> (recursive) to retrieve one if possible.
    /// </summary>
    /// <returns>
    /// <c>null</c> if no <c>double</c> value could be extracted out of the specified typed value.
    /// </returns>
    protected double? CoerceAndUnwrapNumericValue(VBTypedValue value)
    {
        var depth = 0;
        return value is VBNumericTypedValue numValue
            ? numValue.NumericValue
            : value is INumericCoercion coercibleValue
                ? coercibleValue.AsCoercedDouble(ref depth)?.NumericValue
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

    protected abstract VBTypedValue? EvaluateOperationResult(VBExecutionContext context, VBBinaryOperatorExpression expression, VBType effectiveType, VBTypedValue lhs, VBTypedValue rhs);

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
