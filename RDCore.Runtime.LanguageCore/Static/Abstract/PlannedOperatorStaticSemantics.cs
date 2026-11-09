using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.Runtime.LanguageCore.Plans;
using RDCore.SDK.Semantics;

namespace RDCore.Runtime.LanguageCore.Static.Abstract;

public abstract record class PlannedOperatorStaticSemantics : StaticSemantics, IStaticSemantics
{
    protected abstract string Operator { get; }

    public sealed override StaticSemanticsEvaluationResult DetermineDeclaredType(StaticEvaluationContext context, ExpressionNode expression, params VBType[] operandDeclaredTypes)
    {
        var isLateBound = operandDeclaredTypes.Any(operand => operand is VBVariantType or VBObjectType);
        if (isLateBound)
        {
            return operandDeclaredTypes.Any(operand => operand is VBArrayType or VBUserDefinedType)
                ? StaticSemanticsEvaluationResult.Error(GetStaticTypeMismatchErrorInfo(expression, operandDeclaredTypes))
                : StaticSemanticsEvaluationResult.Success(operandDeclaredTypes.Any(operand => operand is VBVariantType) ? VBVariantType.TypeInfo : VBUnknownType.TypeInfo);
        }

        var plan = operandDeclaredTypes.Length == 1
            ? OperationPlanner.ForDeclared(Operator, operandDeclaredTypes[(int)InputIndex.UnaryOperand])
            : OperationPlanner.ForDeclared(Operator, operandDeclaredTypes[(int)InputIndex.BinaryLeftOperand], operandDeclaredTypes[(int)InputIndex.BinaryRightOperand]);

        return plan.EffectiveType is { } effectiveType
            ? StaticSemanticsEvaluationResult.Success(effectiveType)
            : StaticSemanticsEvaluationResult.Error(GetStaticTypeMismatchErrorInfo(expression, operandDeclaredTypes));
    }
}
