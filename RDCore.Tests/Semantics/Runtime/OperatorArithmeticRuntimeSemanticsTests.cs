using NSubstitute;
using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.Runtime.Semantics.Operators;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Context;
using RDCore.SDK.Services.VerboseMessages;
using System.Reflection;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// Base for the arithmetic operator runtime-semantics characterization matrix. Mirrors the
/// static-semantics harness: drive an operator over an (effective type, lhs, rhs) grid — with the
/// operands already at the effective type, as the evaluation pipeline hands them to
/// <c>EvaluateExpressionResult</c> — and assert the result's type + managed value, or the error id.
/// </summary>
public abstract class OperatorArithmeticRuntimeSemanticsTests
{
    protected static readonly SyntaxNodeId NodeId = new(TestUri.TestModuleUri().AbsolutePath, [42]);

    protected static IVerboseMessageBuilder Formatter() => Substitute.For<IVerboseMessageBuilder>();
    protected static ILetCoercionRuntimeSemanticsProvider FakeProvider() => Substitute.For<ILetCoercionRuntimeSemanticsProvider>();

    private static readonly VBBinaryOperatorExpressionNode ThrowawayBinary = new(
        "+", default, TestLocations.TestLocation,
        [
            new LiteralExpressionNode(default, TestLocations.TestLocationLHS, new VBIntegerValue((short)0)),
            new LiteralExpressionNode(default, TestLocations.TestLocationRHS, new VBIntegerValue((short)0)),
        ]);

    private static readonly VBUnaryOperatorExpressionNode ThrowawayUnary = new(
        "-", default, TestLocations.TestLocation,
        [new LiteralExpressionNode(default, TestLocations.TestLocationLHS, new VBIntegerValue((short)0))]);

    private static readonly MethodInfo BinaryEval = typeof(BinaryArithmeticOperatorRuntimeSemantics).GetMethod(
        "EvaluateExpressionResult",
        BindingFlags.Instance | BindingFlags.NonPublic,
        binder: null,
        [typeof(ISymbolResolver), typeof(BinaryArithmeticOperatorSemanticContext), typeof(VBOperatorExpression), typeof(OperatorEvaluationFrame)],
        modifiers: null)!;

    private static readonly MethodInfo UnaryEval = typeof(UnaryArithmeticOperatorRuntimeSemantics).GetMethod(
        "EvaluateExpressionResult",
        BindingFlags.Instance | BindingFlags.NonPublic,
        binder: null,
        [typeof(ISymbolResolver), typeof(UnaryArithmeticOperatorSemanticContext), typeof(VBOperatorExpression), typeof(OperatorEvaluationFrame)],
        modifiers: null)!;

    protected static RuntimeSemanticsEvaluationResult Evaluate(
        BinaryArithmeticOperatorRuntimeSemantics op, VBType effectiveType, VBTypedValue lhs, VBTypedValue rhs)
    {
        var frame = new OperatorEvaluationFrame(NodeId, [lhs, rhs], effectiveType);
        return (RuntimeSemanticsEvaluationResult)BinaryEval.Invoke(
            op, [null, new BinaryArithmeticOperatorSemanticContext(), ThrowawayBinary, frame])!;
    }

    protected static RuntimeSemanticsEvaluationResult Evaluate(
        UnaryArithmeticOperatorRuntimeSemantics op, VBType effectiveType, VBTypedValue operand)
    {
        var frame = new OperatorEvaluationFrame(NodeId, [operand], effectiveType);
        return (RuntimeSemanticsEvaluationResult)UnaryEval.Invoke(
            op, [null, new UnaryArithmeticOperatorSemanticContext(), ThrowawayUnary, frame])!;
    }

    protected static void AssertResult<TValue>(RuntimeSemanticsEvaluationResult result, object expectedManaged)
        where TValue : VBTypedValue
    {
        Assert.IsFalse(result.IsInternalError, "operator reported an internal error");
        Assert.IsNull(result.ErrorInfo, result.ErrorInfo is null ? "" : ((VBRuntimeErrorId)result.ErrorInfo.ErrorId).ToString());
        Assert.IsInstanceOfType<TValue>(result.Result);
        Assert.AreEqual(expectedManaged, ManagedOf(result.Result!));
    }

    protected static void AssertError(RuntimeSemanticsEvaluationResult result, VBRuntimeErrorId expected)
    {
        Assert.IsNotNull(result.ErrorInfo, "expected a runtime error");
        Assert.AreEqual(expected, (VBRuntimeErrorId)result.ErrorInfo!.ErrorId);
    }

    protected static object ManagedOf(VBTypedValue value) => value switch
    {
        VBBooleanValue v => (bool)v.Value,
        VBByteValue v => v.Value,
        VBIntegerValue v => v.Value,
        VBLongValue v => v.Value,
        VBLongLongValue v => v.Value,
        VBSingleValue v => v.Value,
        VBDoubleValue v => v.Value,
        VBCurrencyValue v => v.Value.Value,
        VBDecimalValue v => v.Value,
        VBDateValue v => v.SerialValue,
        _ => value.RuntimeValue.BoxedValue,
    };
}
