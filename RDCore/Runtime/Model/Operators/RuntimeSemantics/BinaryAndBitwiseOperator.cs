using RDCore.Parsing.Model.Types;
using RDCore.Parsing.Model.Values;

namespace RDCore.Runtime.Model.Operators.RuntimeSemantics;

/// <summary>
/// MS-VBAL 5.6.9.8.2 Binary 'And' Operator
/// </summary>
internal record class BinaryAndBitwiseOperator : BinaryBitwiseOperator
{
    protected override int EvaluateBitwise(int lhs, int rhs) => lhs & rhs;

    protected override VBTypedValue? EvaluateSemanticallly(VBExecutionContext context, VBBinaryOperatorExpression expression, VBType effectiveType, VBTypedValue lhs, VBTypedValue rhs)
    {
        if (lhs is VBNumericTypedValue lhsNumeric && rhs is VBNullValue)
        {
            if (lhsNumeric.NumericValue == 0)
            {
                return VBIntegerValue.Zero;
            }
            else
            {
                return VBNullValue.Null;
            }
        }
        
        if (rhs is VBNumericTypedValue rhsNumeric && lhs is VBNullValue)
        {
            if (rhsNumeric.NumericValue == 0)
            {
                return VBIntegerValue.Zero;
            }
            else
            {
                return VBNullValue.Null;
            }
        }

        return default;
    }
}
