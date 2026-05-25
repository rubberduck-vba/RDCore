using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Runtime;
using RDCore.SDK.Semantics.Runtime.Abstract;

namespace RDCore.SDK.Semantics.Runtime.Expressions;

public record class LiteralExpressionRuntimeSemantics : RuntimeSemantics
{
    private static readonly Lazy<LiteralExpressionRuntimeSemantics> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static LiteralExpressionRuntimeSemantics Instance => _instance.Value;

    public override VBType? DetermineEffectiveType(IVBExecutionContext context, params VBType[] operandDeclaredTypes) 
        => operandDeclaredTypes[0];

    protected override VBTypedValue? EvaluateExpressionResult(IVBExecutionContext context, BoundExpression expression, VBType effectiveType, VBTypedValue[] operands) 
        => ((VBLiteralExpression)expression).StaticValue;
}
