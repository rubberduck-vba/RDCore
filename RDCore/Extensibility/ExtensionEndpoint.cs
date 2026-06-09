using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Runtime;
using RDCore.SDK.Semantics;
using System.Net;

namespace RDCore.Extensibility;

public interface ISemanticsExtensibility
{
    void OnEvaluateOperatorExpression<TContext, TFlags>(
        IVBExecutionContext context, 
        BoundNode<TContext, TFlags> expression, 
        params VBTypedValue[] operands)
    where TContext : SemanticContext<TFlags>, new()
    where TFlags : struct, Enum;

    void OnEvaluateStatementExpression<TContext, TFlags>(
        IVBExecutionContext context,
        BoundNode<TContext, TFlags> expression,
        params VBTypedValue[] operands)
    where TContext : SemanticContext<TFlags>, new()
    where TFlags : struct, Enum;
}

internal class SemanticsExtensiblity : ISemanticsExtensibility
{
    public void OnEvaluateOperatorExpression<TContext, TFlags>(
        IVBExecutionContext context, 
        BoundNode<TContext, TFlags> expression, 
        params VBTypedValue[] operands)
    where TContext : SemanticContext<TFlags>, new()
    where TFlags : struct, Enum
    {
        // TODO
    }
    public void OnEvaluateStatementExpression<TContext, TFlags>(
        IVBExecutionContext context,
        BoundNode<TContext, TFlags> expression,
        params VBTypedValue[] operands)
    where TContext : SemanticContext<TFlags>, new()
    where TFlags : struct, Enum
    {
        // TODO
    }
    public void OnExecuteStep<TContext, TFlags>(
        TContext context,
        BoundNode<TContext, TFlags> expression,
        params VBTypedValue[] operands)
    where TContext : SemanticContext<TFlags>, new()
    where TFlags : struct, Enum
    {
        // TODO
    }
}
