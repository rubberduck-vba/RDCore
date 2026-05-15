using RDCore.Parsing.Model.Types;
using RDCore.Parsing.Model.Values;

namespace RDCore.Runtime.Model.Operators.RuntimeSemantics;

internal record class UnaryNotOperatorRuntimeSemantics : UnaryOperatorRuntimeSemantics
{
    protected override VBTypedValue? EvaluateOperationResult(VBExecutionContext context, VBOperatorExpression expression, VBType effectiveType, VBTypedValue[] operands)
    {
        if (effectiveType is IIntegralNumericType)
        {
            if (operands[0] is VBNumericTypedValue num)
            {
                var result = ~(long)num.NumericValue;
                return ((VBNumericTypedValue)effectiveType.CreateValue(expression.Symbol)).WithValue(result) as VBTypedValue;
            }
        }
        else if (effectiveType is VBNullType)
        {
            return VBNullValue.Null;
        }

        return default;
    }
}