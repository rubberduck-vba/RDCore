using NSubstitute;
using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.Runtime.Semantics.Operators;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Analysis;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Context;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// Base for the arithmetic operator runtime-semantics characterization matrix: drives the operator
/// through its public <c>Evaluate</c> entry point (effective-type determination, operand validation
/// and let-coercion, then evaluation), the same path the interpreter uses.
/// </summary>
public abstract class OperatorArithmeticRuntimeSemanticsTests
{
    protected static readonly SyntaxNodeId NodeId = new(TestUri.TestModuleUri().AbsolutePath, [42]);

    protected static IVerboseMessageBuilder Formatter() => Substitute.For<IVerboseMessageBuilder>();

    /// <summary>
    /// A let-coercion provider stand-in for tests that don't exercise coercion itself: every operand
    /// passes through unchanged, except a <see cref="VBDateValue"/> source, which converts to its
    /// double serial value — the one conversion the arithmetic operators' own dispatch requires even
    /// when the operator's effective type is nominally <see cref="VBDateType"/>.
    /// </summary>
    protected static ILetCoercionRuntimeSemanticsProvider FakeProvider()
    {
        var provider = Substitute.For<ILetCoercionRuntimeSemanticsProvider>();
        provider.EvaluateLetCoercionSemantics(default!, default!, default)
            .ReturnsForAnyArgs(call =>
            {
                var source = call.ArgAt<LetCoercionStackFrame>(2).SourceValue;
                return LetCoercionResult.Success(source is VBDateValue date ? new VBDoubleValue(date.SerialValue) : source);
            });
        return provider;
    }

    /// <summary>
    /// The real Numeric, String, Date and Boolean let-coercion strategies — for tests that need an
    /// operand's own let-coercion to genuinely run (and potentially fail), rather than the identity
    /// passthrough of <see cref="FakeProvider"/>.
    /// </summary>
    protected static ILetCoercionRuntimeSemanticsProvider RealCoercionProvider()
    {
        var fmt = Substitute.For<IVerboseMessageBuilder>();
        var handle = new ProviderHandle();
        ILetCoercionRuntimeSemantics[] strategies =
        [
            new VBNumericLetCoercionTypeRuntimeSemantics(fmt, handle),
            new VBStringLetCoercionRuntimeSemantics(fmt, handle),
            new VBDateLetCoercionRuntimeSemantics(handle, fmt),
            new VBBooleanLetCoercionRuntimeSemantics(handle, fmt),
        ];
        var provider = new LetCoercionRuntimeSemanticsProvider(strategies, fmt);
        handle.Inner = provider;
        return provider;
    }

    /// <summary>Breaks the provider ⇄ strategy construction cycle (strategies take the provider itself for recursive coercions).</summary>
    private sealed class ProviderHandle : ILetCoercionRuntimeSemanticsProvider
    {
        public ILetCoercionRuntimeSemanticsProvider Inner { get; set; } = default!;
        public LetCoercionResult EvaluateLetCoercionSemantics(ISymbolResolver resolver, VBOperatorExpression expression, LetCoercionStackFrame frame)
            => Inner.EvaluateLetCoercionSemantics(resolver, expression, frame);
        public LetCoercionAnalysisContext Analyze(ISymbolResolver resolver, ILetCoercionSemanticContextBuilder builder, VBOperatorExpression expression, LetCoercionStackFrame frame)
            => Inner.Analyze(resolver, builder, expression, frame);
    }

    private static readonly VBBinaryOperatorExpressionNode ThrowawayBinary = new(
        "+", default, TestLocations.TestLocation,
        [
            new LiteralExpressionNode(default, TestLocations.TestLocationLHS, new VBIntegerValue((short)0)),
            new LiteralExpressionNode(default, TestLocations.TestLocationRHS, new VBIntegerValue((short)0)),
        ]);

    private static readonly VBUnaryOperatorExpressionNode ThrowawayUnary = new(
        "-", default, TestLocations.TestLocation,
        [new LiteralExpressionNode(default, TestLocations.TestLocationLHS, new VBIntegerValue((short)0))]);

    protected static RuntimeSemanticsEvaluationResult Evaluate(
        BinaryArithmeticOperatorRuntimeSemantics op, VBTypedValue lhs, VBTypedValue rhs)
        => op.Evaluate(null!, new BinaryArithmeticOperatorSemanticContext(), ThrowawayBinary, lhs, rhs);

    protected static RuntimeSemanticsEvaluationResult Evaluate(
        UnaryArithmeticOperatorRuntimeSemantics op, VBTypedValue operand)
        => op.Evaluate(null!, new UnaryArithmeticOperatorSemanticContext(), ThrowawayUnary, operand);

    /// <summary>Runs step 1 of the operator pipeline: resolves the effective value type from operand value types.</summary>
    protected static DetermineOperatorEffectiveTypeResult DetermineEffectiveType(
        BinaryArithmeticOperatorRuntimeSemantics op, VBType lhsType, VBType rhsType)
    {
        var frame = new OperatorEvaluationFrame(NodeId, [lhsType.DefaultValue, rhsType.DefaultValue], VBUnknownType.TypeInfo);
        return op.DetermineOperatorEffectiveType(null!, new BinaryArithmeticOperatorSemanticContext(), ThrowawayBinary, frame);
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
