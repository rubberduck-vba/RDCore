using RDCore.SDK.Model;
using RDCore.Runtime.LanguageCore.Static.Abstract;

namespace RDCore.Runtime.LanguageCore.Static.Operators;

// MS-VBAL §5.6.9.8.1
public sealed record class UnaryLogicalOperatorStaticSemantics : PlannedOperatorStaticSemantics
{
    protected override string Operator => Tokens.LogicalNotOp;
}
