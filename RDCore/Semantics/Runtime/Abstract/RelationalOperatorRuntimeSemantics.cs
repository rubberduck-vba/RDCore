using RDCore.Parsing;
using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Types.Intrinsic;
using RDCore.Parsing.Model.Values.Abstract;
using RDCore.Parsing.Model.Values.Intrinsic;
using RDCore.Runtime;
using RDCore.Runtime.Model.Operators;
using RDCore.Semantics.Runtime.Operators;

namespace RDCore.Semantics.Runtime.Abstract;

internal abstract record class RelationalOperatorRuntimeSemantics : BinaryOperatorRuntimeSemantics
{
    protected sealed override VBType? DetermineOperatorEffectiveType(VBType lhs, VBType rhs)
    {
        return lhs switch
        {
            VBByteType when rhs is VBByteType or VBStringType or VBEmptyType => VBByteType.TypeInfo,
            VBByteType or VBStringType or VBEmptyType when rhs is VBByteType => VBByteType.TypeInfo,

            VBBooleanType when rhs is VBBooleanType or VBStringType => VBBooleanType.TypeInfo,
            VBBooleanType or VBStringType when rhs is VBBooleanType => VBBooleanType.TypeInfo,

            VBIntegerType when rhs is VBByteType or VBBooleanType or VBIntegerType or VBStringType or VBEmptyType => VBIntegerType.TypeInfo,
            VBByteType or VBBooleanType or VBIntegerType or VBStringType or VBEmptyType when rhs is VBIntegerType => VBIntegerType.TypeInfo,
            VBBooleanType when rhs is VBByteType or VBEmptyType => VBIntegerType.TypeInfo,
            VBByteType or VBEmptyType when rhs is VBBooleanType => VBIntegerType.TypeInfo,

            VBByteType or VBBooleanType or VBIntegerType or VBStringType or VBEmptyType
                when rhs is VBIntegerType => VBIntegerType.TypeInfo,
            
            VBBooleanType when rhs is VBByteType or VBEmptyType => VBIntegerType.TypeInfo,
            VBByteType or VBEmptyType when rhs is VBBooleanType => VBIntegerType.TypeInfo,
            
            VBEmptyType when rhs is VBEmptyType => VBIntegerType.TypeInfo,

            VBLongType when rhs is VBByteType or VBBooleanType or VBIntegerType or VBLongType or VBStringType or VBEmptyType => VBLongType.TypeInfo,
            VBByteType or VBBooleanType or VBIntegerType or VBLongType or VBStringType or VBEmptyType when rhs is VBLongType => VBLongType.TypeInfo,

            VBLongLongType when rhs is IIntegralNumericType or VBStringType or VBEmptyType => VBLongLongType.TypeInfo,
            IIntegralNumericType or VBStringType or VBEmptyType when rhs is VBLongLongType => VBLongLongType.TypeInfo,

            VBSingleType when rhs is VBByteType or VBBooleanType or VBIntegerType or VBSingleType or VBDoubleType or VBStringType or VBEmptyType => VBSingleType.TypeInfo,
            VBByteType or VBBooleanType or VBIntegerType or VBSingleType or VBDoubleType or VBStringType or VBEmptyType when rhs is VBSingleType => VBSingleType.TypeInfo,

            VBSingleType when rhs is VBLongType => VBDoubleType.TypeInfo,
            VBLongType when rhs is VBSingleType => VBDoubleType.TypeInfo,
            VBDoubleType when rhs is IIntegralNumericType or VBDoubleType or VBStringType or VBEmptyType => VBDoubleType.TypeInfo,
            IIntegralNumericType or VBDoubleType or VBStringType or VBEmptyType when rhs is VBDoubleType => VBDoubleType.TypeInfo,

            VBStringType when rhs is VBStringType or VBEmptyType => VBStringType.TypeInfo,
            VBStringType or VBEmptyType when rhs is VBStringType => VBStringType.TypeInfo,

            VBCurrencyType when rhs is IIntegralNumericType or IFloatingPointNumericType or VBCurrencyType or VBStringType or VBEmptyType => VBCurrencyType.TypeInfo,
            IIntegralNumericType or IFloatingPointNumericType or VBCurrencyType or VBStringType or VBEmptyType when rhs is VBCurrencyType => VBCurrencyType.TypeInfo,

            VBDateType when rhs is IIntegralNumericType or IFloatingPointNumericType or VBCurrencyType or VBStringType or VBDateType or VBEmptyType => VBDateType.TypeInfo,
            IIntegralNumericType or IFloatingPointNumericType or VBCurrencyType or VBStringType or VBDateType or VBEmptyType when rhs is VBDateType => VBDateType.TypeInfo,

            VBDecimalType when rhs is INumericType or VBStringType or VBDateType or VBEmptyType => VBDecimalType.TypeInfo,
            INumericType or VBStringType or VBDateType or VBEmptyType when rhs is VBDecimalType => VBDecimalType.TypeInfo,

            VBNullType when rhs is INumericType or VBStringType or VBDateType or VBEmptyType or VBNullType => VBNullType.TypeInfo,
            INumericType or VBStringType or VBDateType or VBEmptyType or VBNullType when rhs is VBNullType => VBNullType.TypeInfo,

            VBErrorType when rhs is VBErrorType => VBErrorType.TypeInfo, 
            
            VBErrorType when rhs is not VBErrorType => null, // type mismatch
            not VBErrorType when rhs is VBErrorType => null, // type mismatch
            
            _ => default
        };
    }

    protected sealed override VBTypedValue? EvaluateOperationResult(VBExecutionContext context, VBBinaryOperatorExpression expression, VBType effectiveType, VBTypedValue lhs, VBTypedValue rhs)
    {
        VBBooleanValue? CoerceAndCompare(out double lhsNumeric, out double rhsNumeric) 
        {
            lhsNumeric = default;
            rhsNumeric = default;
            if (CoerceAndUnwrapNumericValue(lhs) is double lhsNum &&
                CoerceAndUnwrapNumericValue(rhs) is double rhsNum)
            {
                lhsNumeric = lhsNum;
                rhsNumeric = rhsNum;

                var result = ComparisonOp(lhsNumeric, rhsNumeric);
                return new VBBooleanValue(expression.Symbol).WithValue(result);
            }
            return default;
        }

        if (effectiveType is VBByteType or VBIntegerType or VBLongType or VBLongLongType or VBCurrencyType or VBDecimalType)
        {
            return CoerceAndCompare(out _, out _);
        }
        else if (effectiveType is VBSingleType or VBDoubleType)
        {
            var result = CoerceAndCompare(out var lhsNumeric, out var rhsNumeric);
            if (double.IsNaN(lhsNumeric))
            {
                throw VBRuntimeErrorException.Overflow(expression.Left.Location.Range);
            }
            if (double.IsNaN(rhsNumeric))
            {
                throw VBRuntimeErrorException.Overflow(expression.Right.Location.Range);
            }

            return result;
        }
        else if (effectiveType is VBBooleanType)
        {
            return CoerceAndCompare(out _, out _);
        }
        else if (effectiveType is VBStringType)
        {
            // FIXME this is wrong...
            // TODO get OptionCompare value from context
            // - Option Compare Text: case insensitive
            // - Option Compare Binary: case sensitive
            if (CoerceAndUnwrapStringValue(lhs) is string lhsString && CoerceAndUnwrapStringValue(rhs) is string rhsString)
            {
                var result = ComparisonOp(lhsString, rhsString, StringComparison.InvariantCultureIgnoreCase);
                return new VBBooleanValue(expression.Symbol).WithValue(result);
            }
        }
        else if (effectiveType is VBNullType)
        {
            return VBNullValue.Null;
        }
        else if (effectiveType is VBErrorType)
        {
            return CoerceAndCompare(out _, out _);
        }

        return default;
    }

    protected abstract bool ComparisonOp(string lhs, string rhs, StringComparison comparison);
    protected abstract bool ComparisonOp(double lhs, double rhs);
}