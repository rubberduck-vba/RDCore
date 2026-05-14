using RDCore.Parsing.Model.Types;
using RDCore.Parsing.Model.Values;

namespace RDCore.Runtime.Model.Operators.RuntimeSemantics;

/// <summary>
/// MS-VBAL 5.6.9.8.3 Binary 'Or' Operator
/// </summary>
internal record class BinaryOrBitwiseOperator : BinaryBitwiseOperator
{
    protected override int EvaluateBitwise(int lhs, int rhs) => lhs | rhs;

    protected override VBTypedValue? EvaluateSemanticallly(VBExecutionContext context, VBBinaryOperatorExpression expression, VBType effectiveType, VBTypedValue lhs, VBTypedValue rhs)
    {
        return lhs switch
        {
            VBTypedValue when lhs.TypeInfo is IIntegralNumericType && rhs is VBNullValue => lhs,
            VBNullValue when rhs.TypeInfo is IIntegralNumericType => rhs,
            _ => default
        };
    }
}
