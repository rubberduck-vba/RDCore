using NSubstitute;
using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.Runtime.Semantics.Operators;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Runtime.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Analysis;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Context;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// Base for the Let-coercion operator (RD-VBAL §5.6.9.9) runtime-semantics characterization matrix:
/// drives the operator through its public <c>Evaluate</c> entry point. The right-hand operand is a
/// <see cref="VBTypeDescValue"/> naming the coercion target type, not an ordinary value.
/// </summary>
public abstract class OperatorLetCoerceRuntimeSemanticsTests : OperatorArithmeticRuntimeSemanticsTests
{
    // Identity must be a real SyntaxNodeId (see OperatorConcatRuntimeSemanticsTests for why): the real
    // coercion provider's recursion guard hashes LetCoercionStackFrame, which hashes this Identity.
    private static readonly VBBinaryOperatorExpressionNode ThrowawayBinary = new(
        "__c()_op", NodeId, TestLocations.TestLocation,
        [
            new LiteralExpressionNode(default, TestLocations.TestLocationLHS, new VBLongValue(0)),
            new LiteralExpressionNode(default, TestLocations.TestLocationRHS, new VBLongValue(0)),
        ]);

    protected static RuntimeSemanticsEvaluationResult Evaluate(
        BinaryLetCoerceOperatorRuntimeSemantics op, VBTypedValue source, VBType targetType)
        => op.Evaluate(null!, new ConversionOperationSemanticContext(), ThrowawayBinary, source, new VBTypeDescValue(targetType));

    /// <summary>Runs step 1 of the operator pipeline: resolves the effective type from the coercion target.</summary>
    protected static DetermineOperatorEffectiveTypeResult DetermineEffectiveType(
        BinaryLetCoerceOperatorRuntimeSemantics op, VBType targetType)
    {
        var frame = new OperatorEvaluationFrame(
            NodeId, [VBLongType.TypeInfo.DefaultValue, new VBTypeDescValue(targetType)], VBUnknownType.TypeInfo);
        return op.DetermineOperatorEffectiveType(null!, new ConversionOperationSemanticContext(), ThrowawayBinary, frame);
    }

    /// <summary>
    /// The real Numeric, String, Date and Boolean let-coercion strategies — the operator's own job is
    /// delegation, but delegating to a fake identity passthrough would never exercise an actual
    /// coercion.
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
}
