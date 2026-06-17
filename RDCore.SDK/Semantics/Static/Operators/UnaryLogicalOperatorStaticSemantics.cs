using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.SDK.Semantics.Static.Operators;

/// <summary>
/// <strong>MS-VBAL 5.6.9.3</strong> Unary Operator static semantics
/// </summary>
public record class UnaryLogicalOperatorStaticSemantics : StaticSemantics
{
    /// <summary>
    /// Determines a static <c>VBType</c> from specified operands.
    /// </summary>
    /// <param name="resolver">The static context containing the available static memory space.</param>
    /// <param name="expression">The <em>expression node</em> being evaluated.</param>
    /// <param name="operandDeclaredTypes">The declared type of each operand involved in the evaluation.</param>
    /// <returns>
    /// A <see cref="StaticSemanticsEvaluationResult"/> encapsulating the resulting <see cref="VBType"/> if successful, or <see cref="VBCompileErrorInfo"/> error metadata otherwise.
    /// </returns>
    public override StaticSemanticsEvaluationResult DetermineDeclaredType(ISymbolResolver resolver, BoundExpression expression, params VBType[] operandDeclaredTypes) 
        => operandDeclaredTypes[(int)InputIndex.UnaryOperand] switch
        {
            VBByteType => VBByteType.TypeInfo,
            VBBooleanType => VBBooleanType.TypeInfo,
            VBIntegerType => VBIntegerType.TypeInfo,
            IFloatingPointNumericType or IFixedPointNumericType or VBLongType or VBFixedStringType or VBStringType or VBDateType => VBLongType.TypeInfo,
            VBLongLongType => VBLongLongType.TypeInfo,
            VBVariantType => VBVariantType.TypeInfo,
            _ => (VBType?)null
        } is VBType result
                ? StaticSemanticsEvaluationResult.Success(result)
                : StaticSemanticsEvaluationResult.Error(GetStaticTypeMismatchErrorInfo(expression, [operandDeclaredTypes[(int)InputIndex.UnaryOperand]]));
}
