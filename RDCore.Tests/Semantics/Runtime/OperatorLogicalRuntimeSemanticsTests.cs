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
using System.Reflection;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// Base for the logical/bitwise operator runtime-semantics characterization matrix — the result is
/// computed in the effective integral type's own CLR representation. Mirrors the arithmetic harness.
/// </summary>
public abstract class OperatorLogicalRuntimeSemanticsTests : OperatorArithmeticRuntimeSemanticsTests
{
    private static readonly VBBinaryOperatorExpressionNode ThrowawayBinary = new(
        "And", default, TestLocations.TestLocation,
        [
            new LiteralExpressionNode(default, TestLocations.TestLocationLHS, new VBLongValue(0)),
            new LiteralExpressionNode(default, TestLocations.TestLocationRHS, new VBLongValue(0)),
        ]);

    private static readonly VBUnaryOperatorExpressionNode ThrowawayUnary = new(
        "Not", default, TestLocations.TestLocation,
        [new LiteralExpressionNode(default, TestLocations.TestLocationLHS, new VBLongValue(0))]);

    private static readonly MethodInfo BinaryEval = typeof(BinaryLogicalOperatorRuntimeSemantics).GetMethod(
        "EvaluateExpressionResult",
        BindingFlags.Instance | BindingFlags.NonPublic,
        binder: null,
        [typeof(ISymbolResolver), typeof(BinaryLogicalOperatorSemanticContext), typeof(VBOperatorExpression), typeof(OperatorEvaluationFrame)],
        modifiers: null)!;

    private static readonly MethodInfo UnaryEval = typeof(UnaryLogicalOperatorRuntimeSemantics).GetMethod(
        "EvaluateExpressionResult",
        BindingFlags.Instance | BindingFlags.NonPublic,
        binder: null,
        [typeof(ISymbolResolver), typeof(UnaryLogicalOperatorSemanticContext), typeof(VBOperatorExpression), typeof(OperatorEvaluationFrame)],
        modifiers: null)!;

    protected static RuntimeSemanticsEvaluationResult Evaluate(
        BinaryLogicalOperatorRuntimeSemantics op, VBType effectiveType, VBTypedValue lhs, VBTypedValue rhs)
    {
        var frame = new OperatorEvaluationFrame(NodeId, [lhs, rhs], effectiveType);
        return (RuntimeSemanticsEvaluationResult)BinaryEval.Invoke(
            op, [null, new BinaryLogicalOperatorSemanticContext(), ThrowawayBinary, frame])!;
    }

    protected static RuntimeSemanticsEvaluationResult Evaluate(
        UnaryLogicalOperatorRuntimeSemantics op, VBType effectiveType, VBTypedValue operand)
    {
        var frame = new OperatorEvaluationFrame(NodeId, [operand], effectiveType);
        return (RuntimeSemanticsEvaluationResult)UnaryEval.Invoke(
            op, [null, new UnaryLogicalOperatorSemanticContext(), ThrowawayUnary, frame])!;
    }

    /// <summary>Runs step 1 of the operator pipeline: resolves the effective value type from operand value types.</summary>
    protected static DetermineOperatorEffectiveTypeResult DetermineEffectiveType(
        BinaryLogicalOperatorRuntimeSemantics op, VBType lhsType, VBType rhsType)
    {
        var frame = new OperatorEvaluationFrame(NodeId, [lhsType.DefaultValue, rhsType.DefaultValue], VBUnknownType.TypeInfo);
        return op.DetermineOperatorEffectiveType(null!, new BinaryLogicalOperatorSemanticContext(), ThrowawayBinary, frame);
    }
}
