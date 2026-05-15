using RDCore.Parsing.Model.Expressions.Operators.StaticContext;
using RDCore.Parsing.Model.Types;
using RDCore.Parsing.Model.Values;
using RDCore.Server;

namespace RDCore.Runtime.Model.Operators.RuntimeSemantics;

/// <summary>
/// MS-VBAL 5.6.9.3.4 Binary '*' Operator (runtime semantics)
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
                var doubleValue = lhsValue * rhsValue;
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
