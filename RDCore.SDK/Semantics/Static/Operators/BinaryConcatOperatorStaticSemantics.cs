using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.SDK.Semantics.Static.Operators;

/// <summary>
/// <strong>MS-VBAL 5.6.9.4</strong> Binary '&amp;' Operator (static semantics)
/// </summary>
public record class BinaryConcatOperatorStaticSemantics : StaticSemantics
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
    {
        var lhs = operandDeclaredTypes[(int)InputIndex.BinaryLeftOperand];
        var rhs = operandDeclaredTypes[(int)InputIndex.BinaryRightOperand];
        
        return lhs switch
        {
            VBNumericType or VBFixedStringType or VBStringType or VBDateType or VBNullType when rhs is VBNumericType or VBFixedStringType or VBStringType or VBDateType
                => StaticSemanticsEvaluationResult.Success(VBStringType.TypeInfo),
            VBNumericType or VBFixedStringType or VBStringType or VBDateType when rhs is VBNumericType or VBFixedStringType or VBStringType or VBDateType or VBNullType
                => StaticSemanticsEvaluationResult.Success(VBStringType.TypeInfo),

            VBType and not VBArrayType and not VBUserDefinedType when rhs is VBVariantType
                => StaticSemanticsEvaluationResult.Success(VBVariantType.TypeInfo),
            VBVariantType when rhs is VBType and not VBArrayType and not VBUserDefinedType
                => StaticSemanticsEvaluationResult.Success(VBVariantType.TypeInfo),

            _ => StaticSemanticsEvaluationResult.Error(GetStaticTypeMismatchErrorInfo(expression, operandDeclaredTypes))
        };
    }
}