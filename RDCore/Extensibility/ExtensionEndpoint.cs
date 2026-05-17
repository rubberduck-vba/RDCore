using RDCore.Parsing.Model.Values.Abstract;
using RDCore.Runtime;
using RDCore.Runtime.Model.Operators;
using System;
using System.Collections.Generic;
using System.Text;

namespace RDCore.Extensibility;

internal interface ISemanticsExtensibility
{
    void OnEvaluateOperatorExpression(VBExecutionContext context, VBOperatorExpression expression, params VBTypedValue[] operands);
}

internal class SemanticsExtensiblity : ISemanticsExtensibility
{
    public void OnEvaluateOperatorExpression(VBExecutionContext context, VBOperatorExpression expression, params VBTypedValue[] operands)
    {
        throw new NotImplementedException();
    }
}
