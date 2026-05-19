using RDCore.SDK.Semantics.Runtime.Abstract;

namespace RDCore.SDK.Semantics.Runtime.Operators;

/// <summary>
/// MS-VBAL 5.6.9.5.4 Binary '>' Operator
/// </summary>
public sealed record class GreaterThanRelationalOperatorRuntimeSemantics : RelationalOperatorRuntimeSemantics
{
    protected override bool ComparisonOp(string lhs, string rhs, StringComparison comparison) => lhs.CompareTo(rhs, comparison) > 0;
    protected override bool ComparisonOp(double lhs, double rhs) => lhs.CompareTo(rhs) > 0;
}
