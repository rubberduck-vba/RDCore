using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Runtime;
using RDCore.SDK.Semantics;

namespace RDCore.Extensibility;

internal interface ISemanticsExtensibility
{
    void OnEvaluateOperatorExpression<TContext, TFlags>(IVBExecutionContext context, VBOperatorExpression<TContext, TFlags> expression, params VBTypedValue[] operands)
        where TContext : SemanticContext<TFlags>, new()
        where TFlags : struct, Enum;
}

internal class SemanticsExtensiblity : ISemanticsExtensibility
{
    public void OnEvaluateOperatorExpression<TContext, TFlags>(IVBExecutionContext context, VBOperatorExpression<TContext, TFlags> expression, params VBTypedValue[] operands)
        where TContext : SemanticContext<TFlags>, new()
        where TFlags : struct, Enum
    {
    }
}
