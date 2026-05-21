using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Expressions.Operators;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;

namespace RDCore.SDK.Semantics.Runtime.Operators;

/// <summary>
/// MS-VBAL 5.6.9.3.7 Binary '^' Operator (runtime semantics)
/// </summary>
public record class BinaryExponentOperatorRuntimeSemantics : BinaryOperatorRuntimeSemantics
{
    protected override VBType? DetermineOperatorEffectiveType(VBType lhs, VBType rhs)
    {
        return lhs switch
        {
            VBNumericType or VBStringType or VBDateType or VBEmptyType
                when rhs is VBNumericType or VBStringType or VBDateType or VBEmptyType => VBDoubleType.TypeInfo,

            _ => base.DetermineOperatorEffectiveType(lhs, rhs)
        };
    }

    protected override VBTypedValue? EvaluateExpressionResult(IVBExecutionContext context, VBBinaryOperatorExpression expression, VBType effectiveType, VBTypedValue lhs, VBTypedValue rhs)
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
                    // if LHS is zero and RHS is negative, we must raise error 5.
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
