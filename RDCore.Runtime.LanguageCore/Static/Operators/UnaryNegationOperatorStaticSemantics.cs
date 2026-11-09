using RDCore.SDK.Model;
using RDCore.Runtime.LanguageCore.Static.Abstract;

namespace RDCore.Runtime.LanguageCore.Static.Operators;

// MS-VBAL §5.6.9.3.1
public sealed record class UnaryNegationOperatorStaticSemantics : UnaryArithmeticOperatorStaticSemantics
{
    protected override string Operator => Tokens.NegationOp;
}
