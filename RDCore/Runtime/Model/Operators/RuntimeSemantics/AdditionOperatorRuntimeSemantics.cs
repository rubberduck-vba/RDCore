using RDCore.Parsing.Model.Types;
using RDCore.Parsing.Model.Values;
using RDCore.Server;

namespace RDCore.Runtime.Model.Operators.RuntimeSemantics;

internal record class AdditionOperatorRuntimeSemantics : BinaryOperatorRuntimeSemantics
{
    protected override VBType? DetermineOperatorEffectiveType(VBType lhs, VBType rhs)
    {
        if (lhs is VBStringType && rhs is VBStringType)
        {
            return VBStringType.TypeInfo;
        }

        return base.DetermineOperatorEffectiveType(lhs, rhs);
    }

    protected override VBTypedValue? EvaluateOperationResult(VBExecutionContext context, VBBinaryOperatorExpression expression, VBType effectiveType, VBTypedValue lhs, VBTypedValue rhs)
    {
        if (effectiveType is INumericType)
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
                var doubleValue = lhsValue + rhsValue;
                return (VBTypedValue)effectiveType.CreateNumericValue(expression.Symbol).WithValue(doubleValue);
            }
        }
        else if (effectiveType is VBDateType)
        {
            if (lhs is VBDateValue)
            {
                context.AddDiagnostic(RDCoreDiagnostic.ImplicitDateSerialConversion(expression.Left.Location.Range));
            }
            if (rhs is VBDateValue)
            {
                context.AddDiagnostic(RDCoreDiagnostic.ImplicitDateSerialConversion(expression.Right.Location.Range));
            }

            if (CoerceAndUnwrapNumericValue(lhs) is double lhsValue &&
                CoerceAndUnwrapNumericValue(rhs) is double rhsValue)
            {
                // the Double value is the sum of the operands.
                // the result is the Double value Let-coerced to Date.
                // if coercion to Date overflows and either operand is Variant (or both), the result is the Double value.
                var doubleValue = lhsValue + rhsValue;
                if ((doubleValue < VBDateValue.MinSerial || doubleValue > VBDateValue.MaxSerial) &&
                    lhs is VBVariantValue || rhs is VBVariantValue)
                {
                    return new VBDoubleValue(expression.Symbol).WithValue(doubleValue);
                }

                return new VBDateValue(expression.Symbol).WithValue(doubleValue);
            }
        }
        else if (effectiveType is VBStringType)
        {
            if (lhs is not VBStringValue)
            {
                context.AddDiagnostic(RDCoreDiagnostic.ImplicitStringCoercion(expression.Left.Location.Range, lhs.TypeInfo));
            }
            if (rhs is not VBStringValue)
            {
                context.AddDiagnostic(RDCoreDiagnostic.ImplicitStringCoercion(expression.Right.Location.Range, rhs.TypeInfo));
            }
            if (lhs is VBStringValue && rhs is VBStringValue)
            {
                context.AddDiagnostic(RDCoreDiagnostic.AmbiguousConcatenation(expression.Location.Range));
            }

            if (CoerceAndUnwrapStringValue(lhs) is string lhsValue &&
                CoerceAndUnwrapStringValue(rhs) is string rhsValue)
            {
                var result = $"{lhsValue}{rhsValue}";
                return new VBStringValue(expression.Symbol).WithValue(result);
            }
        }
        else if (effectiveType is VBNullType)
        {
            return VBNullValue.Null;
        }

        return default;
    }
}
