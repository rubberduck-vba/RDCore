using RDCore.Parsing;
using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Types.Intrinsic;
using RDCore.Parsing.Model.Values.Abstract;
using RDCore.Parsing.Model.Values.Intrinsic;
using RDCore.Runtime;
using RDCore.Runtime.Model.Operators;

namespace RDCore.Semantics.Runtime.Operators;

/// <summary>
/// MS-VBAL 5.6.9.3.7 Binary '^' Operator (runtime semantics)
/// </summary>
internal record class BinaryExponentOperatorRuntimeSemantics : BinaryOperatorRuntimeSemantics
{
    protected override VBType? DetermineOperatorEffectiveType(VBType lhs, VBType rhs)
    {
        return lhs switch
        {
            INumericType or VBStringType or VBDateType or VBEmptyType
                when rhs is INumericType or VBStringType or VBDateType or VBEmptyType => VBDoubleType.TypeInfo,

            _ => base.DetermineOperatorEffectiveType(lhs, rhs)
        };
    }

    protected override VBTypedValue? EvaluateOperationResult(VBExecutionContext context, VBBinaryOperatorExpression expression, VBType effectiveType, VBTypedValue lhs, VBTypedValue rhs)
    {
        if (effectiveType is VBDoubleType)
        {
            if (CoerceAndUnwrapNumericValue(lhs) is double lhsValue &&
                CoerceAndUnwrapNumericValue(rhs) is double rhsValue)
            {
                if (lhsValue == 0 && rhsValue == 0)
                {
                    return new VBDoubleValue(expression.Symbol).WithValue(1);
                }

                if (lhsValue == 0 && rhsValue < 0)
                {
                    // MS-VBAL: if LHS is zero and RHS is negative, raise error 5.
                    throw VBRuntimeErrorException.InvalidProcedureCallOrArgument(expression.Location.Range);
                }

                var result = Math.Pow(lhsValue, rhsValue);
                return new VBDoubleValue(expression.Symbol).WithValue(result);
            }
        }
        else if (effectiveType is VBNullType)
        {
            return VBNullValue.Null;
        }

        return default;
    }
}
