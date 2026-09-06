using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics.Operators;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Context;
using RDCore.SDK.Semantics.Flags;
using System.Reflection;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// Base for the relational operator runtime-semantics characterization matrix — the comparison is
/// exact in the effective type's own representation and yields a <see cref="VBBooleanValue"/>.
/// </summary>
public abstract class OperatorRelationalRuntimeSemanticsTests : OperatorArithmeticRuntimeSemanticsTests
{
    private static readonly VBBinaryOperatorExpressionNode ThrowawayBinary = new(
        "=", default, TestLocations.TestLocation,
        [
            new LiteralExpressionNode(default, TestLocations.TestLocationLHS, new VBLongValue(0)),
            new LiteralExpressionNode(default, TestLocations.TestLocationRHS, new VBLongValue(0)),
        ]);

    private static readonly MethodInfo BinaryEval = typeof(BinaryRelationalOperatorRuntimeSemantics).GetMethod(
        "EvaluateExpressionResult",
        BindingFlags.Instance | BindingFlags.NonPublic,
        binder: null,
        [
            typeof(ISymbolResolver),
            typeof(BinaryOperatorSemanticContext<ComparisonOperatorSemanticFlags>),
            typeof(VBOperatorExpression),
            typeof(OperatorEvaluationFrame),
        ],
        modifiers: null)!;

    protected static RuntimeSemanticsEvaluationResult Evaluate(
        BinaryRelationalOperatorRuntimeSemantics op, VBType effectiveType, VBTypedValue lhs, VBTypedValue rhs)
    {
        var frame = new OperatorEvaluationFrame(NodeId, [lhs, rhs], effectiveType);
        return (RuntimeSemanticsEvaluationResult)BinaryEval.Invoke(
            op, [null, new BinaryOperatorSemanticContext<ComparisonOperatorSemanticFlags>(), ThrowawayBinary, frame])!;
    }
}
