using NSubstitute;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Runtime.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Analysis;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Context;
using RDCore.SDK.Semantics.Context.Abstract;
using RDCore.Runtime.Semantics.LetCoercion;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// The operator analysis pipeline as one unit: an operator's semantics analyzing its operands through the public
/// <c>Analyze</c> entry point, into a real diagnostics-carrying context builder, with the real let-coercion analysis
/// (<see cref="LetCoercionAnalysisHarness"/>) behind it. Only the session, which the analysis reads a symbol resolver from,
/// is a substitute.
/// </summary>
internal static class OperatorAnalysisHarness
{
    private static IRuntimeSession Session()
    {
        var symbols = Substitute.For<ISessionSymbols>();
        symbols.Resolver.Returns(Substitute.For<ISymbolResolver>());
        var session = Substitute.For<IRuntimeSession>();
        session.Symbols.Returns(symbols);
        return session;
    }

    /// <summary>
    /// Analyzes <paramref name="operands"/> as the operands of <paramref name="expression"/> and builds the semantic context.
    /// </summary>
    public static TContext Analyze<TContext, TFlags>(
        IRuntimeSemantics<TContext, TFlags> semantics, VBOperatorExpression expression, params VBTypedValue[] operands)
        where TContext : SemanticContext<TFlags>, new()
        where TFlags : struct, Enum
    {
        var builder = new SemanticContextBuilder<TContext, TFlags>(Substitute.For<ICoreDiagnosticsFactory>());
        semantics.Analyze(Session(), new ConversionOperationSemanticContext(), builder, expression, operands);
        return builder.Build();
    }

    /// <summary>
    /// Evaluates the same operation, the way it runs: what the analysis of it is meant to describe.
    /// </summary>
    public static RuntimeSemanticsEvaluationResult Evaluate<TContext, TFlags>(
        IRuntimeSemantics<TContext, TFlags> semantics, VBOperatorExpression expression, params VBTypedValue[] operands)
        where TContext : SemanticContext<TFlags>, new()
        where TFlags : struct, Enum
        => semantics.Evaluate(Session(), new TContext(), expression, operands);
}

/// <summary>
/// A let-coercion provider that hands everything to the real one and remembers which coercions were asked to be analyzed,
/// for the tests of which operand's coercion an operator analyzes, and to what.
/// </summary>
internal sealed class RecordingLetCoercionProvider(ILetCoercionRuntimeSemanticsProvider inner) : ILetCoercionRuntimeSemanticsProvider
{
    public List<LetCoercionStackFrame> AnalyzedFrames { get; } = [];
    /// <summary>The builder every analyzed coercion of an operation contributes to: the one the whole operation's conversion facts end up in.</summary>
    public ILetCoercionSemanticContextBuilder? Builder { get; private set; }

    public LetCoercionResult EvaluateLetCoercionSemantics(ISymbolResolver resolver, VBOperatorExpression expression, LetCoercionStackFrame frame)
        => inner.EvaluateLetCoercionSemantics(resolver, expression, frame);

    public LetCoercionAnalysisContext Analyze(ISymbolResolver resolver, ILetCoercionSemanticContextBuilder builder, VBOperatorExpression expression, LetCoercionStackFrame frame)
    {
        AnalyzedFrames.Add(frame);
        Builder = builder;
        return inner.Analyze(resolver, builder, expression, frame);
    }
}
