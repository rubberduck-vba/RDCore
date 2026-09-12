using NSubstitute;
using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.Runtime.Semantics.Operators;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Analysis;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Context;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// Base for the <c>&amp;</c> (concatenation) operator runtime-semantics characterization matrix:
/// drives the operator through its public <c>Evaluate</c> entry point (effective-type determination,
/// operand validation and let-coercion, then evaluation), the same path the interpreter uses.
/// </summary>
public abstract class OperatorConcatRuntimeSemanticsTests : OperatorArithmeticRuntimeSemanticsTests
{
    // Identity must be a real SyntaxNodeId (not default): the real coercion provider's recursion
    // guard hashes LetCoercionStackFrame, which hashes this node's Identity — a default SyntaxNodeId's
    // uninitialized ImmutableArray field throws on GetHashCode() (same shape as the NSubstitute struct
    // gotcha in FakeProvider's callers, but hit for real instead of through a mock).
    private static readonly VBBinaryOperatorExpressionNode ThrowawayBinary = new(
        "&", NodeId, TestLocations.TestLocation,
        [
            new LiteralExpressionNode(default, TestLocations.TestLocationLHS, new VBStringValue("")),
            new LiteralExpressionNode(default, TestLocations.TestLocationRHS, new VBStringValue("")),
        ]);

    protected static RuntimeSemanticsEvaluationResult Evaluate(
        BinaryConcatOperatorRuntimeSemantics op, VBTypedValue lhs, VBTypedValue rhs)
        => op.Evaluate(null!, new ConcatOperationSemanticContext(), ThrowawayBinary, lhs, rhs);

    /// <summary>Runs step 1 of the operator pipeline: resolves the effective value type from operand value types.</summary>
    protected static DetermineOperatorEffectiveTypeResult DetermineEffectiveType(
        BinaryConcatOperatorRuntimeSemantics op, VBType lhsType, VBType rhsType)
    {
        var frame = new OperatorEvaluationFrame(NodeId, [lhsType.DefaultValue, rhsType.DefaultValue], VBUnknownType.TypeInfo);
        return op.DetermineOperatorEffectiveType(null!, new ConcatOperationSemanticContext(), ThrowawayBinary, frame);
    }

    /// <summary>
    /// The real MS-VBAL 5.5.1.2.4 string-coercion strategy. Unlike arithmetic/relational/logical —
    /// where the effective type matches one of the operands' own CLR representation, so an identity
    /// passthrough fake is enough — Concat's effective type is always String (or Null), so a fake
    /// provider would never actually exercise concatenation for a non-string operand.
    /// </summary>
    protected static ILetCoercionRuntimeSemanticsProvider StringCoercionProvider()
    {
        var fmt = Substitute.For<IVerboseMessageBuilder>();
        var handle = new ProviderHandle();
        var provider = new LetCoercionRuntimeSemanticsProvider([new VBStringLetCoercionRuntimeSemantics(fmt, handle)], fmt);
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
