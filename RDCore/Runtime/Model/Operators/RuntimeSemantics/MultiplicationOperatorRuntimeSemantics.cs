using RDCore.Parsing.Model.Types;
using RDCore.Parsing.Model.Values;
using RDCore.Server;

namespace RDCore.Runtime.Model.Operators.RuntimeSemantics;

/// <summary>
/// MS-VBAL 5.6.9.3.4 '*' Operator
/// </summary>
internal record class MultiplicationOperatorRuntimeSemantics : BinaryOperatorRuntimeSemantics
{
    protected override VBType? DetermineOperatorEffectiveType(VBType lhs, VBType rhs)
    {
        return lhs switch
        {
            VBCurrencyType when rhs is VBSingleType or VBDoubleType or VBStringType or VBFixedStringType => VBDoubleType.TypeInfo,
            VBSingleType or VBDoubleType or VBStringType or VBFixedStringType when rhs is VBCurrencyType => VBDoubleType.TypeInfo,
            VBDateType when rhs is INumericType or VBStringType or VBFixedStringType or VBDateType => VBDoubleType.TypeInfo,
            INumericType or VBStringType or VBFixedStringType or VBDateType when rhs is VBDateType => VBDoubleType.TypeInfo,
            _ => base.DetermineOperatorEffectiveType(lhs, rhs)
        };
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

            if (numericLhs.HasValue && numericRhs.HasValue)
            {
                var result = numericLhs.Value * numericRhs.Value;
                return (VBTypedValue)effectiveType.CreateNumericValue(expression.Symbol).WithValue(result);
            }
        }
        else if (effectiveType is VBNullType)
        {
            return VBNullValue.Null;
        }

        return default;
    }
}
