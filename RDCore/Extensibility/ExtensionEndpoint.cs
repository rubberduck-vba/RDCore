using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Runtime;

namespace RDCore.Extensibility;

internal interface ISemanticsExtensibility
{
    void OnEvaluateOperatorExpression(IVBExecutionContext context, VBOperatorExpression expression, params VBTypedValue[] operands);
}

internal class SemanticsExtensiblity : ISemanticsExtensibility
{
    public void OnEvaluateOperatorExpression(IVBExecutionContext context, VBOperatorExpression expression, params VBTypedValue[] operands)
    {
        throw new NotImplementedException();
    }
}
