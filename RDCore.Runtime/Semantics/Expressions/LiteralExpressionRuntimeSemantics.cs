using RDCore.Runtime.Semantics.Abstract;
using RDCore.SDK.Model.Expressions;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Runtime;

namespace RDCore.Runtime.Semantics.Expressions;

public record class LiteralExpressionRuntimeSemantics : RuntimeSemantics
{
    public override VBType? DetermineEffectiveType(params VBType[] operandDeclaredTypes) 
        => operandDeclaredTypes[0];

    protected override VBTypedValue? EvaluateExpressionResult(IVBExecutionContext context, ValuedExpression expression, VBType effectiveType, VBTypedValue[] operands) 
        => expression.ResolvedValue;
}
