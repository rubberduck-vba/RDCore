using RDCore.SDK.Model.Expressions.Operators;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;

namespace RDCore.Runtime.Semantics.Operators;

/// <summary>
/// MS-VBAL 5.6.9.3.4 Binary '*' Operator (runtime semantics)
/// </summary>
public record class BinaryMultiplicationOperatorRuntimeSemantics : BinaryOperatorRuntimeSemantics
{
    protected override VBType? DetermineOperatorEffectiveType(VBType lhs, VBType rhs)
    {
        return lhs switch
        {
            VBCurrencyType when rhs is VBSingleType or VBDoubleType or VBFixedStringType or VBStringType => VBDoubleType.TypeInfo,
            VBSingleType or VBDoubleType or VBFixedStringType or VBStringType when rhs is VBCurrencyType => VBDoubleType.TypeInfo,
            VBDateType when rhs is VBNumericType or VBFixedStringType or VBStringType or VBDateType => VBDoubleType.TypeInfo,
            VBNumericType or VBFixedStringType or VBStringType or VBDateType when rhs is VBDateType => VBDoubleType.TypeInfo,
            _ => base.DetermineOperatorEffectiveType(lhs, rhs)
        };
    }

    protected override VBTypedValue? EvaluateExpressionResult(IVBExecutionContext context, VBBinaryOperatorExpression expression, VBType effectiveType, VBTypedValue lhs, VBTypedValue rhs)
    {
        if (effectiveType is INumericType)
        {
            if (CoerceAndUnwrapNumericValue(lhs) is double lhsValue &&
                CoerceAndUnwrapNumericValue(rhs) is double rhsValue)
            {
                var doubleValue = lhsValue * rhsValue;
                return VBTypedValueFactory.CreateValue((VBNumericType)effectiveType, expression.Symbol, doubleValue);
            }
        }
        else if (effectiveType is VBNullType)
        {
            return VBNullValue.Null;
        }

        return default;
    }
}
