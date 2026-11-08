using RDCore.SDK.Model;
using RDCore.Runtime.LanguageCore.Static.Abstract;

namespace RDCore.Runtime.LanguageCore.Static.Operators;

/// <summary>
/// <strong>MS-VBAL 5.6.9.3.5</strong> Binary '/' Operator (static semantics)
/// </summary>
public sealed record class BinaryDivisionOperatorStaticSemantics : BinaryArithmeticOperatorStaticSemantics
{
    protected override string Operator => Tokens.DivisionOp;
}
