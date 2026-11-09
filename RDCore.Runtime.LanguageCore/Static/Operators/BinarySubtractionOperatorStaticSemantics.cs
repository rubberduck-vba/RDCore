using RDCore.SDK.Model;
using RDCore.Runtime.LanguageCore.Static.Abstract;

namespace RDCore.Runtime.LanguageCore.Static.Operators;

/// <summary>
/// <strong>MS-VBAL 5.6.9.3.3</strong> Binary '-' Operator (static semantics)
/// </summary>
public sealed record class BinarySubtractionOperatorStaticSemantics : BinaryArithmeticOperatorStaticSemantics
{
    protected override string Operator => Tokens.SubtractionOp;
}
