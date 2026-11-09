using RDCore.SDK.Model;
using RDCore.Runtime.LanguageCore.Static.Abstract;

namespace RDCore.Runtime.LanguageCore.Static.Operators;

// MS-VBAL §5.6.9.3.6
public sealed record class BinaryIntegerDivisionOperatorStaticSematics : BinaryArithmeticOperatorStaticSemantics
{
    protected override string Operator => Tokens.IntegerDivisionOp;
}
