using RDCore.Semantics.Runtime.Abstract;

namespace RDCore.Semantics.Runtime.Operators;

/// <summary>
/// MS-VBAL 5.6.9.5.1 Binary '=' Operator
/// </summary>
internal sealed record class EqualityRelationalOperatorRuntimeSemantics : RelationalOperatorRuntimeSemantics
{
    protected override bool ComparisonOp(string lhs, string rhs, StringComparison comparison) => lhs.CompareTo(rhs, comparison) == 0;
    protected override bool ComparisonOp(double lhs, double rhs) => lhs.CompareTo(rhs) == 0;
}
