using RDCore.SDK.Model;
using RDCore.Runtime.LanguageCore.Static.Abstract;

namespace RDCore.Runtime.LanguageCore.Static.Operators;

/// <summary>
/// <strong>MS-VBAL 5.6.9.3.8</strong> Binary '^' Operator (static semantics)
/// </summary>
public sealed record class BinaryExponentOperatorStaticSemantics : BinaryArithmeticOperatorStaticSemantics
{
    protected override string Operator => Tokens.PowerOp;
}
