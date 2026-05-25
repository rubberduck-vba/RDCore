using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Symbols.Operators;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;

namespace RDCore.SDK.Semantics.Runtime.Operators;

/// <summary>
/// MS-VBAL 5.6.9.3.1 Unary '+' Operator (runtime semantics)
/// </summary>
/// <remarks>
/// This operator is actually omitted from the MS-VBAL specifications, but is clearly implemented in MS-VBA.
/// The effect of the unary '+' operator is the application of all the let-coercion and null-exception rules to the resulting value.
/// </remarks>
public record class UnaryPlusOperatorRuntimeSemantics : UnaryOperatorRuntimeSemantics
{
    protected override VBType? DetermineOperatorEffectiveType(VBType operand)
    {
        if (operand is VBByteType)
        {
            return VBIntegerType.TypeInfo;
        }

        return base.DetermineOperatorEffectiveType(operand);
    }

    protected override VBTypedValue? EvaluateExpressionResult(IVBExecutionContext context, BoundExpression expression, VBType effectiveType, VBTypedValue[] operands)
    {
        var unary = (VBUnaryOperatorExpression)expression;
        var operand = operands[0];
        if (effectiveType is VBNumericType)
        {
            if (LetCoercionRuntimeSemantics.Evaluate(context, expression, effectiveType, operand) is VBNumericTypedValue coercedNumeric)
            {
                var doubleValue = 0 + coercedNumeric.ManagedValue;
                return VBTypedValueFactory.CreateValue(coercedNumeric.TypeInfo, unary.Symbol, doubleValue);
            }
        }
        else if (effectiveType is VBDateType)
        {
            if (LetCoercionRuntimeSemantics.Evaluate(context, expression, effectiveType, operand) is VBDoubleValue coercedDouble)
            {
                // the Double value is the operand added to 0.
                // the result is the Double value Let-coerced to Date.
                // if coercion to Date overflows and the operand is Variant, the result is the Double value.
                var doubleValue = 0 + coercedDouble.Value;
                if (operand is VBVariantValue && (doubleValue < VBDateValue.MinSerial || doubleValue > VBDateValue.MaxSerial))
                {
                    return new VBDoubleValue(unary.Symbol).WithValue(doubleValue);
                }

                return new VBDateValue(unary.Symbol).WithValue(doubleValue);
            }
        }
        else if (effectiveType is VBNullType)
        {
            return VBNullValue.Null;
        }

        return default;
    }
}
