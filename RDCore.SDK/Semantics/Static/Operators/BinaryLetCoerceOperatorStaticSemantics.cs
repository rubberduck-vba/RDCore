using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.SDK.Semantics.Static.Operators;

/// <summary>
/// <strong>RD-VBAL 5.6.9.1.1 Explicit Coercion Operator</strong>
/// </summary>
/// <remarks>
/// Implements <strong>MS-VBAL 5.6.6 Parenthesized Expression</strong> as a binary operator in <em>non-arithmetic</em> contexts.<br/>
/// </remarks>
public sealed record class BinaryLetCoerceOperatorStaticSemantics() : StaticSemantics()
{
    /// <summary>
    /// Determines a static <see cref="VBType"/> from specified operands.
    /// </summary>
    /// <param name="resolver">The static context containing the available static memory space.</param>
    /// <param name="expression">The <em>expression node</em> being evaluated.</param>
    /// <param name="operandDeclaredTypes">The declared type of each operand involved in the evaluation.</param>
    /// <returns>
    /// A <see cref="StaticSemanticsEvaluationResult"/> encapsulating the resulting <see cref="VBType"/> if successful, or <see cref="VBCompileErrorInfo"/> error metadata otherwise.
    /// </returns>
    public override StaticSemanticsEvaluationResult DetermineDeclaredType(ISymbolResolver resolver, BoundExpression expression, params VBType[] operandDeclaredTypes) 
        => LetCoercionStaticSemantics.Instance.DetermineDeclaredType(resolver, expression, operandDeclaredTypes);
}