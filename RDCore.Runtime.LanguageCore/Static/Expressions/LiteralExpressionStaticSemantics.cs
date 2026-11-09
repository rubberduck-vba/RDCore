using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.Runtime.LanguageCore.Static.Abstract;

namespace RDCore.Runtime.LanguageCore.Static.Expressions;

public record class LiteralExpressionStaticSemantics : IStaticSemantics
{
    private static readonly Lazy<LiteralExpressionStaticSemantics> _instance = new (() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static LiteralExpressionStaticSemantics Instance => _instance.Value;

    // MS-VBAL §5.6.5
    public StaticSemanticsEvaluationResult DetermineDeclaredType(StaticEvaluationContext context, ExpressionNode expression, params VBType[] operandDeclaredTypes)
        => expression is LiteralExpressionNode literal
            ? StaticSemanticsEvaluationResult.Success(literal.StaticValue.TypeInfo)
            : throw new ArgumentException($"Expected a {nameof(LiteralExpressionNode)}.", nameof(expression));
}
