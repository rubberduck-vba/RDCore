using RDCore.SDK.Model;
using RDCore.Runtime.LanguageCore.Static.Abstract;

namespace RDCore.Runtime.LanguageCore.Static.Operators;

// MS-VBAL §5.6.9.3.7
public sealed record class BinaryModuloOperatorStaticSemantics : BinaryArithmeticOperatorStaticSemantics
{
    protected override string Operator => Tokens.ModuloOp;
}
