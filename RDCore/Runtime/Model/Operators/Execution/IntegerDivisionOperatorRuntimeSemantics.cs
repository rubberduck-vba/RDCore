using RDCore.Parsing;
using RDCore.Parsing.Model.Types;
using RDCore.Parsing.Model.Values;
using RDCore.Server;

namespace RDCore.Runtime.Model.Operators.RuntimeSemantics;

/// <summary>
/// MS-VBAL 5.6.9.3.6 Binary '\' Operator and 'Mod' Operator (runtime semantics)
/// </summary>
internal record class IntegerDivisionOperatorRuntimeSemantics : BinaryOperatorRuntimeSemantics
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
                when rhs is INumericType and not VBLongLongType or VBStringType or VBDateType or VBEmptyType => VBLongType.TypeInfo,
            INumericType and not VBLongLongType or VBStringType or VBDateType or VBEmptyType
                when rhs is IFloatingPointNumericType or IFixedPointNumericType or VBStringType or VBDateType => VBLongType.TypeInfo,

            VBLongLongType when rhs is INumericType or VBStringType or VBDateType or VBEmptyType => VBLongLongType.TypeInfo,
            INumericType or VBStringType or VBDateType or VBEmptyType when rhs is VBLongLongType => VBLongLongType.TypeInfo,

            _ => base.DetermineOperatorEffectiveType(lhs, rhs)
        };
    }

    protected override VBTypedValue? EvaluateOperationResult(VBExecutionContext context, VBBinaryOperatorExpression expression, VBType effectiveType, VBTypedValue lhs, VBTypedValue rhs)
    {
        if (effectiveType is VBByteType or VBIntegerType or VBLongType or VBLongLongType)
        {
            if (lhs is not VBNumericTypedValue)
            {
                context.AddDiagnostic(RDCoreDiagnostic.ImplicitNumericCoercion(expression.Left.Location.Range, lhs.TypeInfo, VBDoubleType.TypeInfo));
            }
            if (rhs is not VBNumericTypedValue)
            {
                context.AddDiagnostic(RDCoreDiagnostic.ImplicitNumericCoercion(expression.Right.Location.Range, rhs.TypeInfo, VBDoubleType.TypeInfo));
            }

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
                return (VBTypedValue)effectiveType.CreateNumericValue(expression.Symbol).WithValue(integerValue);
            }
        }
        else if (effectiveType is VBNullType)
        {
            return VBNullValue.Null;
        }

        return default;
    }
}
