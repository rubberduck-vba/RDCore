using RDCore.SDK.Model.Expressions;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;

namespace RDCore.Runtime.Semantics.Operators;

public record class UnaryNotOperatorRuntimeSemantics : UnaryOperatorRuntimeSemantics
{
    protected override VBTypedValue? EvaluateExpressionResult(IVBExecutionContext context, ValuedExpression expression, VBType effectiveType, VBTypedValue[] operands)
    {
        if (effectiveType is IIntegralNumericType)
        {
            if (operands[0] is VBNumericTypedValue num)
            {
                var result = ~(long)num.ManagedValue;
                return VBTypedValueFactory.CreateValue((VBNumericType)effectiveType, num.Symbol, result);
            }
        }
        else if (effectiveType is VBNullType)
        {
            return VBNullValue.Null;
        }

        return default;
    }
}