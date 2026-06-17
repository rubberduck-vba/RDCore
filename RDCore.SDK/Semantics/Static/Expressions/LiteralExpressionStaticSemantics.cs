using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
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
    /// <param name="resolver">The static context containing the available static memory space.</param>
    /// <param name="expression">The <em>expression node</em> being evaluated.</param>
    /// <param name="operandDeclaredTypes">The declared type of the operands.</param>
    public StaticSemanticsEvaluationResult DetermineDeclaredType(ISymbolResolver resolver, BoundExpression expression, params VBType[] operandDeclaredTypes) 
        => StaticSemanticsEvaluationResult.Success(operandDeclaredTypes[(int)InputIndex.UnaryOperand]);
}
