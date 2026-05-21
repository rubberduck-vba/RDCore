using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Runtime;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.SDK.Semantics.Static.Expressions;

public record class LiteralExpressionStaticSemantics : StaticSemantics
{
    public override VBType? DetermineDeclaredType(IVBExecutionContext context, params VBType[] operandDeclaredTypes) => operandDeclaredTypes[0];
}
