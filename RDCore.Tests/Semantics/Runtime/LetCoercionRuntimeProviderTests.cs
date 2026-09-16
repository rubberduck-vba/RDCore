using NSubstitute;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Runtime.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Analysis;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// Integration tests for <see cref="LetCoercionRuntimeSemanticsProvider"/> — strategy dispatch and
/// the recursion guard. The provider is not DI-wired anywhere yet, so the graph is built by hand.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.5.1.2 Let-coercion (runtime)")]
public sealed class LetCoercionRuntimeProviderTests : LetCoercionRuntimeSemanticsTests
{
    /// <summary>Breaks the provider ⇄ strategy construction cycle for the test.</summary>
    private sealed class ProviderHandle : ILetCoercionRuntimeSemanticsProvider
    {
        public ILetCoercionRuntimeSemanticsProvider Inner { get; set; } = default!;
        public LetCoercionResult EvaluateLetCoercionSemantics(ISymbolResolver r, VBOperatorExpression e, LetCoercionStackFrame f)
            => Inner.EvaluateLetCoercionSemantics(r, e, f);
        public LetCoercionAnalysisContext Analyze(ISymbolResolver r, ILetCoercionSemanticContextBuilder b, VBOperatorExpression e, LetCoercionStackFrame f)
            => Inner.Analyze(r, b, e, f);
    }

    private static LetCoercionRuntimeSemanticsProvider BuildProvider()
    {
        var fmt = Substitute.For<IVerboseMessageBuilder>();
        var handle = new ProviderHandle();
        ILetCoercionRuntimeSemantics[] strategies =
        [
            new VBNumericLetCoercionTypeRuntimeSemantics(fmt, handle),
            new VBBooleanLetCoercionRuntimeSemantics(handle, fmt),
            new VBDateLetCoercionRuntimeSemantics(handle, fmt),
        ];
        var provider = new LetCoercionRuntimeSemanticsProvider(strategies, fmt);
        handle.Inner = provider;
        return provider;
    }

    private static LetCoercionResult Coerce(VBTypedValue source, VBType destination)
    {
        var frame = new LetCoercionStackFrame(NodeId, InputIndex.CoercionSourceValue, source, new VBTypeDescValue(destination));
        // resolver unused; expression only on error paths — reuse the base throwaway via a public shim
        return BuildProvider().EvaluateLetCoercionSemantics(null!, ThrowawayExpression, frame);
    }

    [TestMethod]
    public void Dispatch_ResolvesTheNumericStrategyForAConcreteNumericDestination()
    {
        // regression: the provider keyed on frame.DestinationTypeDesc.GetType() (always VBTypeDescValue),
        // so it never matched a strategy and always returned TypeMismatch.
        var result = Coerce(new VBDoubleValue(2.67), VBIntegerType.TypeInfo);
        Assert.IsTrue(result.IsSuccess, result.ErrorInfo is null ? "" : ((VBRuntimeErrorId)result.ErrorInfo.ErrorId).ToString());
        Assert.IsInstanceOfType<VBIntegerValue>(result.Result);
        Assert.AreEqual((short)3, ((VBIntegerValue)result.Result!).Value);
    }

    [TestMethod]
    public void Dispatch_UnknownDestination_IsTypeMismatch()
        => Assert.AreEqual((int)VBRuntimeErrorId.TypeMismatch,
            Coerce(new VBDoubleValue(1), VBStringType.TypeInfo).ErrorInfo!.ErrorId);

    [TestMethod]
    public void RecursiveReEntry_WithTheSameFrame_IsOutOfStackSpace()
    {
        // a strategy that (however it got there) calls back into the provider with the exact same
        // frame it was given is the one scenario the recursion guard exists for. No production
        // strategy actually does this by design, so a substitute stands in for one that does.
        var fmt = Substitute.For<IVerboseMessageBuilder>();
        var strategy = Substitute.For<ILetCoercionRuntimeSemantics>();
        strategy.LetCoercionSpecification.Returns(typeof(VBLongType));

        var provider = new LetCoercionRuntimeSemanticsProvider([strategy], fmt);
        strategy.EvaluateLetCoercion(default!, default!, default).ReturnsForAnyArgs(call =>
            provider.EvaluateLetCoercionSemantics(call.ArgAt<ISymbolResolver>(0), call.ArgAt<VBOperatorExpression>(1), call.ArgAt<LetCoercionStackFrame>(2)));

        var frame = new LetCoercionStackFrame(NodeId, InputIndex.CoercionSourceValue, new VBLongValue(5), new VBTypeDescValue(VBLongType.TypeInfo));
        var result = provider.EvaluateLetCoercionSemantics(null!, ThrowawayExpression, frame);

        Assert.IsTrue(result.ErrorInfo is not null, "expected a runtime error");
        Assert.AreEqual((int)VBRuntimeErrorId.OutOfStackSpace, result.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void AfterARecursiveReEntry_TheStackIsCleared_SoALaterUnrelatedCallStillWorks()
        // the recursion guard clears the whole stack on detection (comment: "no need to dig any
        // deeper") rather than leaving it in a partially-unwound state -- confirm a later, entirely
        // unrelated coercion isn't permanently poisoned by an earlier recursive failure.
    {
        var fmt = Substitute.For<IVerboseMessageBuilder>();
        var strategy = Substitute.For<ILetCoercionRuntimeSemantics>();
        strategy.LetCoercionSpecification.Returns(typeof(VBLongType));

        var provider = new LetCoercionRuntimeSemanticsProvider([strategy], fmt);
        var recursedOnce = false;
        strategy.EvaluateLetCoercion(default!, default!, default).ReturnsForAnyArgs(call =>
        {
            if (recursedOnce)
            {
                return LetCoercionResult.Success(new VBLongValue(9));
            }
            recursedOnce = true;
            return provider.EvaluateLetCoercionSemantics(call.ArgAt<ISymbolResolver>(0), call.ArgAt<VBOperatorExpression>(1), call.ArgAt<LetCoercionStackFrame>(2));
        });

        var recursiveFrame = new LetCoercionStackFrame(NodeId, InputIndex.CoercionSourceValue, new VBLongValue(5), new VBTypeDescValue(VBLongType.TypeInfo));
        provider.EvaluateLetCoercionSemantics(null!, ThrowawayExpression, recursiveFrame);

        // the strategy no longer recurses on this second, later call -- confirms only whether the
        // provider's own stack state was reset, not anything about the strategy's own behavior.
        var result = provider.EvaluateLetCoercionSemantics(null!, ThrowawayExpression, recursiveFrame);

        Assert.IsTrue(result.IsSuccess);
    }
}
