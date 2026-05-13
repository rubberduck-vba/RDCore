using RDCore.Parsing.Model.Types;
using RDCore.Parsing.Model.Values;
using RDCore.Server;

namespace RDCore.Runtime.Model.Operators.RuntimeSemantics;

internal record class SubtractionOperatorRuntimeSematics : BinaryOperatorRuntimeSemantics
{
    protected override VBType? DetermineOperatorEffectiveType(VBType lhs, VBType rhs)
    {
        if (lhs is VBDateType && rhs is VBDateType)
        {
            return VBDoubleType.TypeInfo;
        }

        return base.DetermineOperatorEffectiveType(lhs, rhs);
    }

    protected override VBTypedValue? EvaluateOperationResult(VBExecutionContext context, VBOperatorExpression expression, VBType effectiveType, VBTypedValue[] operands)
    {
        var lhs = operands[0];
        var rhs = operands[1];
        var binaryOp = (VBBinaryOperatorExpression)expression;

        if (effectiveType is INumericType)
        {
            var depth = 0;
            var numericLhs = lhs is VBNumericTypedValue lhsNum
                ? lhsNum.NumericValue
                : lhs is INumericCoercion lhsCoercion
                    ? lhsCoercion.AsCoercedDouble(ref depth)?.NumericValue
                    : null;

            depth = 0;
            var numericRhs = rhs is VBNumericTypedValue rhsNum
                ? rhsNum.NumericValue
                : rhs is INumericCoercion rhsCoercion
                    ? rhsCoercion.AsCoercedDouble(ref depth)?.NumericValue
                    : null;

            if (lhs is not VBNumericTypedValue)
            {
                context.AddDiagnostic(RDCoreDiagnostic.ImplicitNumericCoercion(binaryOp.Left.Location.Range, lhs.TypeInfo, VBDoubleType.TypeInfo));
            }
            if (rhs is not VBNumericTypedValue)
            {
                context.AddDiagnostic(RDCoreDiagnostic.ImplicitNumericCoercion(binaryOp.Right.Location.Range, rhs.TypeInfo, VBDoubleType.TypeInfo));
            }
            if (lhs is VBDateValue)
            {
                context.AddDiagnostic(RDCoreDiagnostic.ImplicitDateSerialConversion(binaryOp.Left.Location.Range));
            }
            if (rhs is VBDateValue)
            {
                context.AddDiagnostic(RDCoreDiagnostic.ImplicitDateSerialConversion(binaryOp.Right.Location.Range));
            }

            if (numericLhs.HasValue && numericRhs.HasValue)
            {
                var result = numericLhs.Value - numericRhs.Value;
                return (VBTypedValue)effectiveType.CreateNumericValue(expression.Symbol).WithValue(result);
            }
        }
        else if (effectiveType is VBDateType)
        {
            var lhsDepth = 0;
            var rhsDepth = 0;
            if (lhs is INumericCoercion lhsCoercibleNumeric && lhsCoercibleNumeric.AsCoercedDouble(ref lhsDepth) is VBDoubleValue lhsCoercedDouble &&
                rhs is INumericCoercion rhsCoercibleNumeric && rhsCoercibleNumeric.AsCoercedDouble(ref rhsDepth) is VBDoubleValue rhsCoercedDouble)
            {
                if (lhs is VBDateValue)
                {
                    context.AddDiagnostic(RDCoreDiagnostic.ImplicitDateSerialConversion(binaryOp.Left.Location.Range));
                }
                if (rhs is VBDateValue)
                {
                    context.AddDiagnostic(RDCoreDiagnostic.ImplicitDateSerialConversion(binaryOp.Right.Location.Range));
                }

                // the Double value subtracts RHS from LHS.
                // the result is the Double value Let-coerced to Date.
                // if coercion to Date overflows and either operand is Variant (or both), the result is the Double value.
                var doubleValue = lhsCoercedDouble.Value - rhsCoercedDouble.Value;
                if ((doubleValue < VBDateValue.MinSerial || doubleValue > VBDateValue.MaxSerial) &&
                    lhs is VBVariantValue || rhs is VBVariantValue)
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
