using NSubstitute;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.Runtime.Semantics.Operators;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// <see cref="OperatorRuntimeSemanticsProvider"/> dispatches by <c>Token</c> to one instance per
/// operator, built once and reused - never rebuilt per call, the same way
/// <see cref="LetCoercionRuntimeSemanticsProvider"/> owns its own coercion strategies.
/// </summary>
[TestClass]
public sealed class OperatorRuntimeSemanticsProviderTests
{
    private static readonly SyntaxNodeId NodeId = new("file://rdcore-test", [1]);
    private static readonly IRuntimeSession Session = Substitute.For<IRuntimeSession>();

    private static IOperatorRuntimeSemanticsProvider Provider()
        => new OperatorRuntimeSemanticsProvider(Substitute.For<ILetCoercionRuntimeSemanticsProvider>(), Substitute.For<IVerboseMessageBuilder>());

    private static VBBinaryOperatorExpressionNode Binary(string token)
        => new(token, NodeId, TestLocations.TestLocation, new LiteralExpressionNode(NodeId, TestLocations.TestLocation, new VBLongValue(1)),
            new LiteralExpressionNode(NodeId, TestLocations.TestLocation, new VBLongValue(2)));

    private static VBUnaryOperatorExpressionNode Unary(string token)
        => new(token, NodeId, TestLocations.TestLocation, [new LiteralExpressionNode(NodeId, TestLocations.TestLocation, new VBLongValue(1))]);

    public static IEnumerable<object[]> EveryBinaryToken()
    {
        yield return [Tokens.AdditionOp];
        yield return [Tokens.SubtractionOp];
        yield return [Tokens.MultiplicationOp];
        yield return [Tokens.DivisionOp];
        yield return [Tokens.IntegerDivisionOp];
        yield return [Tokens.ModuloOp];
        yield return [Tokens.PowerOp];
        yield return [Tokens.ConcatOp];
        yield return [Tokens.CompareIsOp];
        yield return [Tokens.CompareEqualOp];
        yield return [Tokens.CompareNotEqualOp];
        yield return [Tokens.CompareGreaterThanOp];
        yield return [Tokens.CompareGreaterThanOrEqualOp];
        yield return [Tokens.CompareLessThanOp];
        yield return [Tokens.CompareLessThanOrEqualOp];
        yield return [Tokens.CompareLikeOp];
        yield return [Tokens.LogicalAndOp];
        yield return [Tokens.LogicalOrOp];
        yield return [Tokens.LogicalXOrOp];
        yield return [Tokens.LogicalEqvOp];
        yield return [Tokens.LogicalImpOp];
    }

    [TestMethod]
    [DynamicData(nameof(EveryBinaryToken))]
    public void EveryBinaryToken_DispatchesToItsOwnOperator_NeverInternalError(string token)
    {
        var result = Provider().EvaluateBinaryOperator(Session, Binary(token), new VBLongValue(1), new VBLongValue(2));

        Assert.IsFalse(result.IsInternalError, $"'{token}' dispatched to InternalError - missing from the provider's switch.");
    }

    [TestMethod]
    [DataRow(Tokens.NegationOp)]
    [DataRow(Tokens.LogicalNotOp)]
    public void EveryUnaryToken_DispatchesToItsOwnOperator_NeverInternalError(string token)
    {
        var result = Provider().EvaluateUnaryOperator(Session, Unary(token), new VBLongValue(1));

        Assert.IsFalse(result.IsInternalError, $"'{token}' dispatched to InternalError - missing from the provider's switch.");
    }

    [TestMethod]
    public void AnUnrecognizedBinaryToken_IsAnInternalError()
        => Assert.IsTrue(Provider().EvaluateBinaryOperator(Session, Binary("?"), new VBLongValue(1), new VBLongValue(2)).IsInternalError);

    [TestMethod]
    public void AnUnrecognizedUnaryToken_IsAnInternalError()
        => Assert.IsTrue(Provider().EvaluateUnaryOperator(Session, Unary("?"), new VBLongValue(1)).IsInternalError);

    [TestMethod]
    public void TheSameProviderInstance_HandlesRepeatedEvaluationsOfDifferentOperators()
        // each operator's runtime semantics is built once, in the provider's own constructor - reusing
        // one provider instance across unrelated operator tokens must not corrupt or rebuild anything.
    {
        var provider = Provider();

        var addition = provider.EvaluateBinaryOperator(Session, Binary(Tokens.AdditionOp), new VBLongValue(1), new VBLongValue(2));
        var negation = provider.EvaluateUnaryOperator(Session, Unary(Tokens.NegationOp), new VBLongValue(1));
        var additionAgain = provider.EvaluateBinaryOperator(Session, Binary(Tokens.AdditionOp), new VBLongValue(3), new VBLongValue(4));

        Assert.AreEqual(3, ((VBLongValue)addition.Result!).Value);
        Assert.AreEqual(-1, ((VBLongValue)negation.Result!).Value);
        Assert.AreEqual(7, ((VBLongValue)additionAgain.Result!).Value);
    }
}
