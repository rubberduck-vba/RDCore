using RDCore.SDK.Model.Expressions;
using RDCore.SDK.Model.Expressions.Operators;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Intrinsic;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;

namespace RDCore.SDK.Semantics.Runtime.Operators;

public record class UnaryNotOperatorRuntimeSemantics : UnaryOperatorRuntimeSemantics
{
    protected override VBTypedValue? EvaluateExpressionResult(IVBExecutionContext context, ValuedExpression expression, VBType effectiveType, VBTypedValue[] operands)
    {
        if (effectiveType is IIntegralNumericType)
        {
            if (operands[0] is VBNumericTypedValue num)
            {
                var result = ~(long)num.ManagedValue;
                return ((VBNumericTypedValue)effectiveType.CreateValue(((VBOperatorExpression)expression).Symbol)).WithValue(result) as VBTypedValue;
            }
        }
        else if (effectiveType is VBNullType)
        {
            return VBNullValue.Null;
        }

        return default;
    }
}