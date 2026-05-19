using RDCore.SDK.Model.Expressions.Operators;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;
using RDCore.SDK.Semantics.Runtime.Abstract;

namespace RDCore.SDK.Semantics.Runtime.Operators;

/// <summary>
/// MS-VBAL 5.6.9.8.3 Binary 'Or' Operator
/// </summary>
public record class BinaryOrBitwiseOperatorRuntimeSemantics : BinaryBitwiseOperatorRuntimeSemantics
{
    protected override int EvaluateBitwise(int lhs, int rhs) => lhs | rhs;

    protected override VBTypedValue? EvaluateSemanticallly(IVBExecutionContext context, VBBinaryOperatorExpression expression, VBType effectiveType, VBTypedValue lhs, VBTypedValue rhs)
    {
        return lhs switch
        {
            VBTypedValue when lhs.TypeInfo is IIntegralNumericType && rhs is VBNullValue => lhs,
            VBNullValue when rhs.TypeInfo is IIntegralNumericType => rhs,
            _ => default
        };
    }
}
