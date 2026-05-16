using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Types.Intrinsic;
using RDCore.Parsing.Model.Values.Abstract;
using RDCore.Parsing.Model.Values.Intrinsic;
using RDCore.Runtime;
using RDCore.Runtime.Model.Operators;
using RDCore.Semantics.Diagnostics;

namespace RDCore.Semantics.Runtime.Operators;

internal record class BinarySubtractionOperatorRuntimeSematics : BinaryOperatorRuntimeSemantics
{
    protected override VBType? DetermineOperatorEffectiveType(VBType lhs, VBType rhs)
    {
        if (lhs is VBDateType && rhs is VBDateType)
        {
            return VBDoubleType.TypeInfo;
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
                var doubleValue = lhsValue - rhsValue;
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
