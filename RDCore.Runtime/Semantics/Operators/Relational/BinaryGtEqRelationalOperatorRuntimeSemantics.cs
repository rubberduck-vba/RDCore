using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Runtime.Semantics.Operators.Relational;

/// <summary>
/// MS-VBAL 5.6.9.5.6 Binary '>=' Operator
/// </summary>
public sealed record class BinaryGtEqRelationalOperatorRuntimeSemantics(
    ILetCoercionRuntimeSemanticsProvider LetCoercionSemanticsProvider,
    IVerboseMessageBuilder FormatterService)
    : BinaryRelationalOperatorRuntimeSemantics(LetCoercionSemanticsProvider, FormatterService)
{
    protected override bool ComparisonOp(VBStringValue lhs, VBStringValue rhs, StringComparisonRules rules) => rules.Compare(lhs, rhs) >= 0;
    protected override bool ComparisonOp<T>(T lhs, T rhs) => lhs >= rhs;
}
