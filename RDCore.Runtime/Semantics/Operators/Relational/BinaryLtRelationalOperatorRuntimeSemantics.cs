using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Runtime;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Runtime.Semantics.Operators.Relational;

/// <summary>
/// MS-VBAL 5.6.9.5.3 Binary '<' Operator
/// </summary>
public sealed record class BinaryLtRelationalOperatorRuntimeSemantics(
    ILetCoercionRuntimeSemanticsProvider LetCoercionSemanticsProvider,
    IVerboseMessageBuilder FormatterService)
    : BinaryRelationalOperatorRuntimeSemantics(LetCoercionSemanticsProvider, FormatterService)
{
    protected override bool ComparisonOp(string lhs, string rhs, StringComparisonRules rules) => rules.Comparer.Compare(lhs, rhs) < 0;
    protected override bool ComparisonOp<T>(T lhs, T rhs) => lhs < rhs;
}
