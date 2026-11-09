using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.Runtime.LanguageCore.Static.Abstract;
using RDCore.SDK.Semantics;

namespace RDCore.Runtime.LanguageCore.Static.Operators;

// MS-VBAL §5.6.6; RD-VBAL §5.6.6
public sealed record class UnaryPrecedenceOperatorStaticSemantics() : StaticSemantics()
{
    public override StaticSemanticsEvaluationResult DetermineDeclaredType(StaticEvaluationContext context, ExpressionNode expression, params VBType[] operandDeclaredTypes)
        => StaticSemanticsEvaluationResult.Success(operandDeclaredTypes[(int)InputIndex.UnaryOperand]);
}
