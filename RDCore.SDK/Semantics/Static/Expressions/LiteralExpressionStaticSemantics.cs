using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.SDK.Semantics.Static.Expressions;

public record class LiteralExpressionStaticSemantics : IStaticSemantics
{
    private static readonly Lazy<LiteralExpressionStaticSemantics> _instance = new (() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static LiteralExpressionStaticSemantics Instance => _instance.Value;

    /// <summary>
    /// MS-VBAL 5.6.5 Literal Expressions (static semantics) 
    /// The declared type of a <em>literal expression</em> is that of the specified token.
    /// </summary>
    /// <param name="context">The compile-time context this expression is evaluated against.</param>
    /// <param name="expression">The <em>expression node</em> being evaluated.</param>
    /// <param name="operandDeclaredTypes">Ignored: a literal has no operands, its type is that of its own token.</param>
    /// <exception cref="ArgumentException"><paramref name="expression"/> is not a <see cref="LiteralExpressionNode"/>.</exception>
    public StaticSemanticsEvaluationResult DetermineDeclaredType(StaticEvaluationContext context, ExpressionNode expression, params VBType[] operandDeclaredTypes)
        => expression is LiteralExpressionNode literal
            ? StaticSemanticsEvaluationResult.Success(literal.StaticValue.TypeInfo)
            : throw new ArgumentException($"Expected a {nameof(LiteralExpressionNode)}.", nameof(expression));
}
