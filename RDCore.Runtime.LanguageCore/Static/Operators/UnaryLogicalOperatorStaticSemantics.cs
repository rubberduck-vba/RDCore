using RDCore.SDK.Model;
using RDCore.Runtime.LanguageCore.Static.Abstract;

namespace RDCore.Runtime.LanguageCore.Static.Operators;

/// <summary>
/// <strong>MS-VBAL 5.6.9.8.1</strong> Unary 'Not' Operator (static semantics)
/// </summary>
public sealed record class UnaryLogicalOperatorStaticSemantics : PlannedOperatorStaticSemantics
{
    protected override string Operator => Tokens.LogicalNotOp;
}
