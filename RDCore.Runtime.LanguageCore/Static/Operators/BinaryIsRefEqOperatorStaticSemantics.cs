using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.Runtime.LanguageCore.Static.Abstract;

namespace RDCore.Runtime.LanguageCore.Static.Operators;

// MS-VBAL §5.6.9.7
public sealed record class BinaryIsRefEqOperatorStaticSemantics : StaticSemantics
{
    public override  StaticSemanticsEvaluationResult DetermineDeclaredType(StaticEvaluationContext context, ExpressionNode expression, params VBType[] operandDeclaredTypes)
    {
        // MS-VBAL: each expression MUST be classified as a value and
        // the declared type of each expression MUST be a specific class, Object, or Variant.

        return operandDeclaredTypes.All(operand => operand is VBClassType or VBObjectType or VBVariantType)
            ? StaticSemanticsEvaluationResult.Success(VBBooleanType.TypeInfo)
            : StaticSemanticsEvaluationResult.Error(GetStaticTypeMismatchErrorInfo(expression, operandDeclaredTypes));
    }
}
