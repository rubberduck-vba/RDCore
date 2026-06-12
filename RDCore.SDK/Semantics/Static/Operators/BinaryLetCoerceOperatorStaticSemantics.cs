using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Runtime;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.SDK.Semantics.Static.Operators
{
    /// <summary>
    /// MS-VBAL: Unspecified. This semantic operator has different token semantics than the arithmetic precedence operator.
    /// </summary>
    public sealed record class BinaryLetCoerceOperatorStaticSemantics() : StaticSemantics()
    {
        public override VBType? DetermineDeclaredType(IVBExecutionContext context, params VBType[] operandDeclaredTypes) 
            => LetCoercionStaticSemantics.Instance.DetermineDeclaredType(context, operandDeclaredTypes);
    }
}