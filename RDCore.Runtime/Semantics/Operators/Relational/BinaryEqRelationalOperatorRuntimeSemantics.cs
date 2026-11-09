using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;

namespace RDCore.Runtime.Semantics.Operators.Relational;

/// <summary>
/// MS-VBAL 5.6.9.5.1 Binary '=' Operator
/// </summary>
public sealed record class BinaryEqRelationalOperatorRuntimeSemantics(
    ILetCoercionRuntimeSemanticsProvider LetCoercionSemanticsProvider,
    IErrorDetailsRecorder ErrorDetails)
    : BinaryRelationalOperatorRuntimeSemantics(LetCoercionSemanticsProvider, ErrorDetails)
{
    protected override string Operator => Tokens.CompareEqualOp;

    protected override bool ComparisonOp(VBStringValue lhs, VBStringValue rhs, StringComparisonRules rules) => rules.Compare(lhs, rhs) == 0;
    protected override bool ComparisonOp<T>(T lhs, T rhs) => lhs == rhs;
}
