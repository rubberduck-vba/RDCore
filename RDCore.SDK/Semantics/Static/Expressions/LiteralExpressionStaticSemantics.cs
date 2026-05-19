using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.SDK.Semantics.Static.Expressions;

public record class LiteralExpressionStaticSemantics : StaticSemantics
{
    public override VBType? DetermineDeclaredType(params VBType[] operandDeclaredTypes) => operandDeclaredTypes[0];
}
