using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Semantics.Static.Abstract;

namespace RDCore.Semantics.Static.Expressions;

internal record class LiteralExpressionStaticSemantics : StaticSemantics
{
    public override VBType? DetermineDeclaredType(params VBType[] operandDeclaredTypes) => operandDeclaredTypes[0];
}
