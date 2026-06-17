using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.SDK.Semantics.Static.Abstract;

/// <summary>
/// Uses pattern-matching rules to encapsulate unary arithmetic operator static semantics as defined in <strong>MS-VBAL 5.6.9.3</strong>.
/// </summary>
/// <remarks>
/// This is implicitly the specification for the unary '+' operator, which is omitted from MS-VBAL.
/// </remarks>
public record class UnaryArithmeticOperatorStaticSemantics : StaticSemantics, IStaticSemantics
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
        => DetermineOperatorStaticType(resolver, expression, operandDeclaredTypes[(int)InputIndex.UnaryOperand]);

    /// <summary>
    /// MS-VBAL 5.6.9.3 Arithmetic Operators (static semantics) 
    /// The operator has the declared type returned by this method, based on the declared type of its operands.
    /// </summary>
    /// <param name="expression">The <em>expression node</em> being evaluated.</param>
    /// <param name="operand">The declared type of the operand.</param>
    protected virtual StaticSemanticsEvaluationResult DetermineOperatorStaticType(ISymbolResolver resolver, BoundExpression expression, VBType operand)  
        => operand switch
        {
            VBByteType => VBByteType.TypeInfo,
            VBBooleanType or VBIntegerType => VBIntegerType.TypeInfo,
            VBLongType => VBLongType.TypeInfo,
            VBLongLongType => VBLongLongType.TypeInfo,
            VBSingleType => VBSingleType.TypeInfo,
            VBDoubleType or VBFixedStringType or VBStringType => VBDoubleType.TypeInfo,
            VBCurrencyType => VBCurrencyType.TypeInfo,
            VBDateType => VBDateType.TypeInfo,
            VBVariantType => VBVariantType.TypeInfo,
            _ => (VBType?)null
        } is VBType result
            ? StaticSemanticsEvaluationResult.Success(result)
            : StaticSemanticsEvaluationResult.Error(GetStaticTypeMismatchErrorInfo(expression, [operand]));
}
