using RDCore.Runtime.Semantics.Abstract;

namespace RDCore.Runtime.Semantics.Operators;

/// <summary>
/// MS-VBAL 5.6.9.5.6 Binary '>=' Operator
/// </summary>
public sealed record class GreaterThanEqRelationalOperatorRuntimeSemantics : RelationalOperatorRuntimeSemantics
{
    protected override bool ComparisonOp(string lhs, string rhs, StringComparison comparison) => lhs.CompareTo(rhs, comparison) >= 0;
    protected override bool ComparisonOp(double lhs, double rhs) => lhs.CompareTo(rhs) >= 0;
}
