using RDCore.Parsing.Model.Types;
using RDCore.Parsing.Model.Values;

namespace RDCore.Runtime.Model.Operators.RuntimeSemantics;

/// <summary>
/// MS-VBAL 5.6.9.8.6 Binary 'Imp' Operator
/// </summary>
internal record class BinaryImpBitwiseOperator : BinaryBitwiseOperator
{
    protected override int EvaluateBitwise(int lhs, int rhs)
    {
        return Convert.ToInt32(~(long)lhs | ~(long)rhs);
    }

    protected override VBTypedValue? EvaluateSemanticallly(VBExecutionContext context, VBBinaryOperatorExpression expression, VBType effectiveType, VBTypedValue lhs, VBTypedValue rhs)
    {
        // TODO refactor this so we can issue diagnostics for bitwise evaluations
        return lhs switch
        {
            VBNumericTypedValue lhsNumeric when lhsNumeric.NumericValue == -1 && rhs is VBNullValue 
                => VBNullValue.Null,

            VBNumericTypedValue lhsNumeric when lhs.TypeInfo is IIntegralNumericType && lhsNumeric.NumericValue != -1 && rhs is VBNullValue 
                => (VBTypedValue)effectiveType.CreateNumericValue(expression.Symbol).WithValue(EvaluateBitwise((int)lhsNumeric.NumericValue, 0)),

            VBNullValue when rhs.TypeInfo is IIntegralNumericType && rhs is VBNumericTypedValue rhsNumeric && rhsNumeric.NumericValue != 0 
                => rhs,

            VBNullValue when rhs is VBNumericTypedValue rhsNumeric && rhsNumeric.NumericValue == 0 
                => VBNullValue.Null,

            _ => default
        };
    }
}
