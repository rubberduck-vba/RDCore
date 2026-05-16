using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Types.Intrinsic;
using RDCore.Parsing.Model.Values.Abstract;
using RDCore.Parsing.Model.Values.Intrinsic;
using RDCore.Runtime;
using RDCore.Runtime.Model.Operators;

namespace RDCore.Semantics.Runtime.Operators;

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