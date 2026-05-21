using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Expressions.Operators;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;

namespace RDCore.Runtime.Semantics.Operators;

/// <summary>
/// MS-VBAL 5.6.9.3.6 Binary '\' Operator and 'Mod' Operator (runtime semantics)
/// </summary>
public record class BinaryIntegerDivisionOperatorRuntimeSemantics : BinaryOperatorRuntimeSemantics
{
    protected override VBType? DetermineOperatorEffectiveType(VBType lhs, VBType rhs)
    {
        return lhs switch
        {
            VBByteType when rhs is VBEmptyType => VBIntegerType.TypeInfo,
            VBEmptyType when rhs is VBByteType => VBIntegerType.TypeInfo,
            VBBooleanType or VBIntegerType 
                when rhs is VBSingleType or VBDoubleType or VBStringType or VBCurrencyType or VBDateType or VBDecimalType => VBIntegerType.TypeInfo,

            IFloatingPointNumericType or IFixedPointNumericType or VBStringType or VBDateType
                when rhs is VBNumericType and not VBLongLongType or VBStringType or VBDateType or VBEmptyType => VBLongType.TypeInfo,
            VBNumericType and not VBLongLongType or VBStringType or VBDateType or VBEmptyType
                when rhs is IFloatingPointNumericType or IFixedPointNumericType or VBStringType or VBDateType => VBLongType.TypeInfo,

            VBLongLongType when rhs is VBNumericType or VBStringType or VBDateType or VBEmptyType => VBLongLongType.TypeInfo,
            VBNumericType or VBStringType or VBDateType or VBEmptyType when rhs is VBLongLongType => VBLongLongType.TypeInfo,

            _ => base.DetermineOperatorEffectiveType(lhs, rhs)
        };
    }

    protected override VBTypedValue? EvaluateExpressionResult(IVBExecutionContext context, VBBinaryOperatorExpression expression, VBType effectiveType, VBTypedValue lhs, VBTypedValue rhs)
    {
        if (effectiveType is VBByteType or VBIntegerType or VBLongType or VBLongLongType)
        {
            if (CoerceAndUnwrapNumericValue(lhs) is double lhsValue &&
                CoerceAndUnwrapNumericValue(rhs) is double rhsValue)
            {
                var divisor = (int)Math.Round(rhsValue, 0, MidpointRounding.ToEven);
                if (divisor == 0)
                {
                    throw VBRuntimeErrorException.DivisionByZero(expression.Right.Location.Range);
                }

                // if the quotient is an integer, the result is the quotient.
                // if the quotient is not an integer, the result is the integer nearest to the quotient that is closer to zero than the quotient.
                var integerValue = (int)Math.Round(lhsValue, 0, MidpointRounding.ToEven) / divisor;
                return VBTypedValueFactory.CreateValue((VBNumericType)effectiveType, expression.Symbol, integerValue);
            }
        }
        else if (effectiveType is VBNullType)
        {
            return VBNullValue.Null;
        }

        return default;
    }
}
