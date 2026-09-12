using RDCore.Runtime.Semantics.Operators;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Context;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// Base for the relational operator runtime-semantics characterization matrix: drives the operator
/// through its public <c>Evaluate</c> entry point (effective-type determination, operand validation
/// and let-coercion, then evaluation), the same path the interpreter uses. Mirrors the arithmetic
/// harness — the comparison is exact in the effective type's own representation and yields a
/// <see cref="VBBooleanValue"/>.
/// </summary>
public abstract class OperatorRelationalRuntimeSemanticsTests : OperatorArithmeticRuntimeSemanticsTests
{
    private static readonly VBBinaryOperatorExpressionNode ThrowawayBinary = new(
        "=", default, TestLocations.TestLocation,
        [
            new LiteralExpressionNode(default, TestLocations.TestLocationLHS, new VBLongValue(0)),
            new LiteralExpressionNode(default, TestLocations.TestLocationRHS, new VBLongValue(0)),
        ]);

    protected static RuntimeSemanticsEvaluationResult Evaluate(
        BinaryRelationalOperatorRuntimeSemantics op, VBTypedValue lhs, VBTypedValue rhs)
        => op.Evaluate(null!, new BinaryOperatorSemanticContext<ComparisonOperatorSemanticFlags>(), ThrowawayBinary, lhs, rhs);
}
