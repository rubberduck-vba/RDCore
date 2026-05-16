using RDCore.Parsing;
using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Types.Intrinsic;
using RDCore.Parsing.Model.Values.Abstract;
using RDCore.Parsing.Model.Values.Intrinsic;
using RDCore.Runtime;
using RDCore.Runtime.Model.Operators;
using RDCore.Semantics.Diagnostics;

namespace RDCore.Semantics.Runtime.Operators;

/// <summary>
/// MS-VBAL 5.6.9.3.5 Binary '/' Operator (runtime semantics)
/// </summary>
internal record class BinaryDivisionOperatorRuntimeSemantics : BinaryOperatorRuntimeSemantics
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

    protected override VBTypedValue? EvaluateOperationResult(VBExecutionContext context, VBBinaryOperatorExpression expression, VBType effectiveType, VBTypedValue lhs, VBTypedValue rhs)
    {
        if (effectiveType is VBDecimalType or VBSingleType or VBDoubleType)
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
                if (rhsValue == 0)
                {
                    // TODO? MS-VBAL trying to be cute here...
                    // * if this expression was within the RHS of a Let statement
                    // * and both operators have a declared type of Double
                    // * the resulting IEEE 754 Double special value (+/- infinity, NaN) is assigned...
                    // * ...BEFORE raising the error.
                    if (effectiveType is VBSingleType or VBDoubleType && lhsValue == 0 && 
                        !(lhs is VBSingleValue or VBDoubleValue or VBStringValue or VBDateValue && rhs is VBEmptyValue))
                    {
                        throw VBRuntimeErrorException.Overflow(expression.Location.Range);
                    }

                    throw VBRuntimeErrorException.DivisionByZero(expression.Right.Location.Range);
                }

                var doubleValue = lhsValue / rhsValue;
                return (VBTypedValue)effectiveType.CreateNumericValue(expression.Symbol).WithValue(doubleValue);
            }
        }
        else if (effectiveType is VBNullType)
        {
            return VBNullValue.Null;
        }

        return default;
    }
}
