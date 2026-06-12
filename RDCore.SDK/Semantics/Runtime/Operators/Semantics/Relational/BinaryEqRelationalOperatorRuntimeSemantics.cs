using RDCore.SDK.Semantics.Runtime.LetCoercion;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.SDK.Semantics.Runtime.Operators.Semantics.Relational
{
    /// <summary>
    /// MS-VBAL 5.6.9.5.1 Binary '=' Operator
    /// </summary>
    public sealed record class BinaryEqRelationalOperatorRuntimeSemantics(
        ILetCoercionRuntimeSemanticsProvider LetCoercionSemanticsProvider,
        IVerboseMessageBuilder FormatterService)
        : BinaryRelationalOperatorRuntimeSemantics(LetCoercionSemanticsProvider, FormatterService)
    {
        protected override bool ComparisonOp(string lhs, string rhs, StringComparison comparison) => lhs.CompareTo(rhs, comparison) == 0;
        protected override bool ComparisonOp(double lhs, double rhs) => lhs.CompareTo(rhs) == 0;
    }
}
