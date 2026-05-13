using RDCore.Parsing;
using RDCore.Parsing.Model.Types;
using RDCore.Parsing.Model.Values;
using RDCore.Server;

namespace RDCore.Runtime.Model.Operators.RuntimeSemantics;

/// <summary>
/// MS-VBAL 5.6.9.3.5 Binary '/' Operator
/// </summary>
internal record class DivisionOperatorRuntimeSemantics : BinaryOperatorRuntimeSemantics
{
    protected override VBType? DetermineOperatorEffectiveType(VBType lhs, VBType rhs)
    {
        return lhs switch
        {
            VBByteType or VBBooleanType or VBIntegerType or VBLongType or VBLongLongType or VBEmptyType
                when rhs is VBByteType or VBBooleanType or VBIntegerType or VBLongType or VBLongLongType or VBEmptyType => VBDoubleType.TypeInfo,

            VBDoubleType or VBStringType or VBCurrencyType or VBDateType
                when rhs is INumericType or VBStringType or VBDateType or VBEmptyType => VBDoubleType.TypeInfo,

            INumericType or VBStringType or VBDateType or VBEmptyType
                when rhs is VBDoubleType or VBStringType or VBCurrencyType or VBDateType => VBDoubleType.TypeInfo,

            _ => base.DetermineOperatorEffectiveType(lhs, rhs)
        };
    }

    protected override VBTypedValue? EvaluateOperationResult(VBExecutionContext context, VBOperatorExpression expression, VBType effectiveType, VBTypedValue[] operands)
    {
        var lhs = operands[0];
        var rhs = operands[1];
        var binaryOp = (VBBinaryOperatorExpression)expression;

        if (effectiveType is VBDecimalType or VBSingleType or VBDoubleType)
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
                if (numericRhs.Value == 0)
                {
                    // TODO? MS-VBAL trying to be cute here...
                    // * if this expression was within the RHS of a Let statement
                    // * and both operators have a declared type of Double
                    // * the resulting IEEE 754 Double special value (+/- infinity, NaN) is assigned...
                    // * ...BEFORE raising the error.
                    if (effectiveType is VBSingleType or VBDoubleType &&
                        numericLhs.Value == 0 && 
                        !(lhs is VBSingleValue or VBDoubleValue or VBStringValue or VBDateValue && rhs is VBEmptyValue))
                    {
                        throw VBRuntimeErrorException.Overflow(binaryOp.Location.Range);
                    }

                    throw VBRuntimeErrorException.DivisionByZero(binaryOp.Right.Location.Range);
                }

                var result = numericLhs.Value / numericRhs.Value;
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
