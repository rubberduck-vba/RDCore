using RDCore.Parsing.Model.Types;
using RDCore.Parsing.Model.Values;

namespace RDCore.Runtime.Model.Operators.RuntimeSemantics;

/// <summary>
/// MS-VBAL 5.6.9.8.6 Binary 'Imp' Operator
/// </summary>
internal record class BinaryImpBitwiseOperator : BinaryBitwiseOperator
{
    protected override int EvaluateBitwise(int lhs, int rhs)
    {
        return (~lhs) | rhs;
    }

    protected override VBTypedValue? EvaluateSemanticallly(VBExecutionContext context, VBBinaryOperatorExpression expression, VBType effectiveType, VBTypedValue lhs, VBTypedValue rhs)
    {
        // NOTE: we ignore the computed effective type, because the runtime semantics are off the values, not just their types.

        if (lhs.TypeInfo is IIntegralNumericType && rhs.TypeInfo is IIntegralNumericType)
        {
            if (CoerceAndUnwrapNumericValue(lhs) is double lhsDouble && 
                CoerceAndUnwrapNumericValue(rhs) is double rhsDouble)
            {
                // result is bitwise imp of operands
                effectiveType = VBIntegerType.TypeInfo;
                var result = EvaluateBitwise(Convert.ToInt32(lhsDouble), Convert.ToInt32(rhsDouble));
                return (VBNumericTypedValue)effectiveType.CreateNumericValue(expression.Symbol).WithValue(result);
            }
        }
        if (CoerceAndUnwrapNumericValue(lhs) is double lhsNumNegative && lhsNumNegative == -1 && rhs is VBNullValue)
        {
            return VBNullValue.Null;
        }
        if (CoerceAndUnwrapNumericValue(lhs) is double lhsNumeric && lhsNumeric != -1 && rhs is VBNullValue)
        {
            // result is bitwise imp of left operand and 0.
            effectiveType = VBIntegerType.TypeInfo;
            var result = EvaluateBitwise(Convert.ToInt32(lhsNumeric), 0);
            return (VBNumericTypedValue)effectiveType.CreateNumericValue(expression.Symbol).WithValue(result);
        }
        if (lhs is VBNullValue && rhs.TypeInfo is IIntegralNumericType && CoerceAndUnwrapNumericValue(rhs) is double rhsNonZero && rhsNonZero != 0)
        {
            // result is the right operand.
            return rhs;
        }
        if (lhs is VBNullValue && CoerceAndUnwrapNumericValue(rhs) is double rhsZero && rhsZero == 0)
        {
            return VBNullValue.Null;
        }
        if (lhs is VBNullValue && rhs is VBNullValue)
        {
            return VBNullValue.Null;
        }

        return default;
    }
}
