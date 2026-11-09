using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Semantics;

namespace RDCore.Runtime.LanguageCore.Static.Abstract;

// MS-VBAL §5.6.9.5
public record class BinaryRelationalOperatorStaticSemantics : StaticSemantics, IStaticSemantics
{
    // MS-VBAL §5.6.9.5
    public override StaticSemanticsEvaluationResult DetermineDeclaredType(
        StaticEvaluationContext context,
        ExpressionNode expression,
        params VBType[] operandDeclaredTypes)
        => DetermineOperatorStaticType(expression, operandDeclaredTypes[(int)InputIndex.BinaryLeftOperand], operandDeclaredTypes[(int)InputIndex.BinaryRightOperand]);

    // MS-VBAL §5.6.9.5
    protected virtual StaticSemanticsEvaluationResult DetermineOperatorStaticType(ExpressionNode expression, VBType lhs, VBType rhs)
        => lhs switch
        {
            not VBArrayType and not VBUserDefinedType and not VBVariantType
                when rhs is not VBArrayType and not VBUserDefinedType and not VBVariantType => VBBooleanType.TypeInfo,

            not VBArrayType and not VBUserDefinedType when rhs is VBVariantType => VBVariantType.TypeInfo,
            VBVariantType when rhs is not VBArrayType and not VBUserDefinedType => VBVariantType.TypeInfo,

            _ => (VBType?)null
        } is VBType result
            ? StaticSemanticsEvaluationResult.Success(result)
            : StaticSemanticsEvaluationResult.Error(GetStaticTypeMismatchErrorInfo(expression, [lhs, rhs]));
}
