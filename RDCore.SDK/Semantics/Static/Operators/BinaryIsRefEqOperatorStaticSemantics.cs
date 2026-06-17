using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.SDK.Semantics.Static.Operators;

/// <summary>
/// <strong>MS-VBAL 5.6.9.7</strong> Binary 'Is' Operator (static semantics)
/// </summary>
public sealed record class BinaryIsRefEqOperatorStaticSemantics : StaticSemantics
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
    public override  StaticSemanticsEvaluationResult DetermineDeclaredType(ISymbolResolver resolver, BoundExpression expression, params VBType[] operandDeclaredTypes)
    {
        // MS-VBAL: each expression MUST be classified as a value and
        // the declared type of each expression MUST be a specific class, Object, or Variant.

        return operandDeclaredTypes.All(operand => operand is VBClassType or VBObjectType or VBVariantType)
            ? StaticSemanticsEvaluationResult.Success(VBBooleanType.TypeInfo)
            : StaticSemanticsEvaluationResult.Error(GetStaticTypeMismatchErrorInfo(expression, operandDeclaredTypes));
    }
}
