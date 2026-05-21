using RDCore.Runtime.Semantics.Abstract;
using RDCore.SDK.Model.Expressions.Operators;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;

namespace RDCore.Runtime.Semantics.Operators;

/// <summary>
/// MS-VBAL 5.6.9.8.6 Binary 'Imp' Operator
/// </summary>
public record class BinaryImpBitwiseOperatorRuntimeSemantics : BinaryBitwiseOperatorRuntimeSemantics
{
    protected override int EvaluateBitwise(int lhs, int rhs)
    {
        return (~lhs) | rhs;
    }

    protected override VBTypedValue? EvaluateSemanticallly(IVBExecutionContext context, VBBinaryOperatorExpression expression, VBType effectiveType, VBTypedValue lhs, VBTypedValue rhs)
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
                return VBTypedValueFactory.CreateValue((VBNumericType)effectiveType, expression.Symbol, result);
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
            return VBTypedValueFactory.CreateValue((VBNumericType)effectiveType, expression.Symbol, result);
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
