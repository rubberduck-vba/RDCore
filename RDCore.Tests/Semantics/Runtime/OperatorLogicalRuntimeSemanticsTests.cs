using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics.Operators;
using RDCore.Runtime.Semantics.Operators.Logical;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Context;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// Base for the logical/bitwise operator runtime-semantics characterization matrix: drives the
/// operator through its public <c>Evaluate</c> entry point (effective-type determination, operand
/// validation and let-coercion, then evaluation), the same path the interpreter uses. Mirrors the
/// arithmetic harness — the result is computed in the effective integral type's own CLR representation.
/// </summary>
public abstract class OperatorLogicalRuntimeSemanticsTests : OperatorArithmeticRuntimeSemanticsTests
{
    // Identity must be a real SyntaxNodeId (see OperatorLetCoerceRuntimeSemanticsTests for why): the
    // real coercion provider's recursion guard hashes LetCoercionStackFrame, which hashes this Identity.
    private static readonly VBBinaryOperatorExpressionNode ThrowawayBinary = new(
        "And", NodeId, TestLocations.TestLocation,
        [
            new LiteralExpressionNode(default, TestLocations.TestLocationLHS, new VBLongValue(0)),
            new LiteralExpressionNode(default, TestLocations.TestLocationRHS, new VBLongValue(0)),
        ]);

    private static readonly VBUnaryOperatorExpressionNode ThrowawayUnary = new(
        "Not", default, TestLocations.TestLocation,
        [new LiteralExpressionNode(default, TestLocations.TestLocationLHS, new VBLongValue(0))]);

    protected static RuntimeSemanticsEvaluationResult Evaluate(
        BinaryLogicalOperatorRuntimeSemantics op, VBTypedValue lhs, VBTypedValue rhs)
        => op.Evaluate(null!, new BinaryLogicalOperatorSemanticContext(), ThrowawayBinary, lhs, rhs);

    protected static RuntimeSemanticsEvaluationResult Evaluate(
        UnaryLogicalOperatorRuntimeSemantics op, VBTypedValue operand)
        => op.Evaluate(null!, new UnaryLogicalOperatorSemanticContext(), ThrowawayUnary, operand);

    /// <summary>Runs step 1 of the operator pipeline: resolves the effective value type from operand value types.</summary>
    protected static DetermineOperatorEffectiveTypeResult DetermineEffectiveType(
        BinaryLogicalOperatorRuntimeSemantics op, VBType lhsType, VBType rhsType)
    {
        var frame = new OperatorEvaluationFrame(NodeId, [lhsType.DefaultValue, rhsType.DefaultValue], VBUnknownType.TypeInfo);
        return op.DetermineOperatorEffectiveType(null!, new BinaryLogicalOperatorSemanticContext(), ThrowawayBinary, frame);
    }
}
