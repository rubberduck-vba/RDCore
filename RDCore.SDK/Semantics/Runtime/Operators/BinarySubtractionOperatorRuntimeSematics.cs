using RDCore.SDK.Model.AST.Operators;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;

namespace RDCore.SDK.Semantics.Runtime.Operators;

public record class BinarySubtractionOperatorRuntimeSematics : BinaryOperatorRuntimeSemantics
{
    protected override VBType? DetermineOperatorEffectiveType(VBType lhs, VBType rhs) => lhs 
        switch {
            VBDateType when rhs is VBDateType => VBDoubleType.TypeInfo,
            _ => base.DetermineOperatorEffectiveType(lhs, rhs)
        };

    protected override VBTypedValue? EvaluateExpressionResult(IVBExecutionContext context, VBBinaryOperatorExpression expression, VBType effectiveType, VBTypedValue lhs, VBTypedValue rhs)
    {
        if (effectiveType is VBNumericType numericEffectiveType)
        {
            if (CoerceAndUnwrapNumericValue(lhs) is double lhsValue &&
                CoerceAndUnwrapNumericValue(rhs) is double rhsValue)
            {
                var doubleValue = lhsValue - rhsValue;
                return VBTypedValueFactory.CreateValue(numericEffectiveType, expression.Symbol, doubleValue);
            }
        }
        else if (effectiveType is VBDateType)
        {
            if (CoerceAndUnwrapNumericValue(lhs) is double lhsValue &&
                CoerceAndUnwrapNumericValue(rhs) is double rhsValue)
            {
                // the Double value subtracts RHS from LHS.
                // the result is the Double value Let-coerced to Date.
                // if coercion to Date overflows and either operand is Variant (or both), the result is the Double value.
                var doubleValue = lhsValue - rhsValue;
                if (VBDateValue.WillOverflow(doubleValue) && (lhs is VBVariantValue || rhs is VBVariantValue))
                {
                    return new VBDoubleValue(expression.Symbol).WithValue(doubleValue);
                }

                return new VBDateValue(expression.Symbol).WithValue(doubleValue);
            }
        }
        else if (effectiveType is VBNullType)
        {
            return VBNullValue.Null;
        }

        return default;
    }
}
