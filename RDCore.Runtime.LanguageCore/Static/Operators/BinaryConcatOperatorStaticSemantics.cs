using RDCore.SDK.Model;
using RDCore.Runtime.LanguageCore.Static.Abstract;

namespace RDCore.Runtime.LanguageCore.Static.Operators;

// MS-VBAL §5.6.9.4
public sealed record class BinaryConcatOperatorStaticSemantics : PlannedOperatorStaticSemantics
{
    protected override string Operator => Tokens.ConcatOp;
}
