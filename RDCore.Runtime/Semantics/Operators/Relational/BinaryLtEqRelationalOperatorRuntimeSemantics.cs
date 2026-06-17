using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Runtime.Semantics.Operators.Relational;

/// <summary>
/// MS-VBAL 5.6.9.5.5 Binary '<=' Operator
/// </summary>
public sealed record class BinaryLtEqRelationalOperatorRuntimeSemantics(
    ILetCoercionRuntimeSemanticsProvider LetCoercionSemanticsProvider,
    IVerboseMessageBuilder FormatterService)
    : BinaryRelationalOperatorRuntimeSemantics(LetCoercionSemanticsProvider, FormatterService)
{
    protected override bool ComparisonOp(string lhs, string rhs, StringComparison comparison) => lhs.CompareTo(rhs, comparison) <= 0;
    protected override bool ComparisonOp(double lhs, double rhs) => lhs.CompareTo(rhs) <= 0;
}
