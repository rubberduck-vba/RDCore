using NSubstitute;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Runtime.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Analysis;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// The let-coercion analysis pipeline as one unit: the real provider over every real strategy, with the real
/// flags builder — the way the pipeline is meant to run, rather than one strategy behind a substitute.
/// </summary>
internal static class LetCoercionAnalysisHarness
{
    // strategies and provider construct each other; this breaks the cycle.
    private sealed class ProviderHandle : ILetCoercionRuntimeSemanticsProvider
    {
        public ILetCoercionRuntimeSemanticsProvider Inner { get; set; } = default!;

        public LetCoercionResult EvaluateLetCoercionSemantics(ISymbolResolver resolver, ExpressionNode expression, LetCoercionStackFrame frame)
            => Inner.EvaluateLetCoercionSemantics(resolver, expression, frame);

        public LetCoercionAnalysisContext Analyze(ISymbolResolver resolver, ILetCoercionSemanticContextBuilder builder, ExpressionNode expression, LetCoercionStackFrame frame)
            => Inner.Analyze(resolver, builder, expression, frame);
    }

    /// <summary>
    /// A provider over every let-coercion strategy the runtime implements.
    /// </summary>
    public static ILetCoercionRuntimeSemanticsProvider BuildProvider()
    {
        var formatter = Substitute.For<IVerboseMessageBuilder>();
        var handle = new ProviderHandle();
        ILetCoercionRuntimeSemantics[] strategies =
        [
            new VBNumericLetCoercionTypeRuntimeSemantics(formatter, handle),
            new VBBooleanLetCoercionRuntimeSemantics(handle, formatter),
            new VBDateLetCoercionRuntimeSemantics(handle, formatter),
            new VBStringLetCoercionRuntimeSemantics(formatter),
            new VBFixedStringLetCoercionRuntimeSemantics(handle, formatter),
            new VBVariantTypeLetCoercionRuntimeSemantics(handle, formatter),
            new VBObjectLetCoercionRuntimeSemantics(handle, formatter),
            new VBEmptyTypeLetCoercionRuntimeSemantics(formatter),
            new VBNullTypeLetCoercionRuntimeSemantics(formatter),
            new VBErrorTypeLetCoercionRuntimeSemantics(handle, formatter),
            new VBUserDefinedTypeLetCoercionRuntimeSemantics(handle, formatter),
            new VBResizableArrayLetCoercionRuntimeSemantics(handle, formatter),
            new VBResizableByteArrayLetCoercionRuntimeSemantics(handle, formatter),
        ];
        var provider = new LetCoercionRuntimeSemanticsProvider(strategies, formatter);
        handle.Inner = provider;
        return provider;
    }

    /// <summary>
    /// What the analysis of a single let-coercion reports: the context it returns, and the flags its builder ended up with.
    /// </summary>
    public readonly record struct Analysis(LetCoercionAnalysisContext Context, LetCoercionSemanticContextFlagsBuilder Builder)
    {
        /// <summary>The conversion flags of the operation.</summary>
        public ConversionSemanticFlags Flags => Builder.Flags;
    }

    /// <summary>
    /// Analyzes the let-coercion of <paramref name="source"/> to <paramref name="destination"/> as the
    /// <paramref name="operand"/> of <paramref name="expression"/>, into <paramref name="builder"/> (a new one, if none is given —
    /// pass one to analyze several operands of one operation into the same context).
    /// </summary>
    public static Analysis Analyze(VBTypedValue source, VBType destination, ExpressionNode expression,
        InputIndex operand = InputIndex.CoercionSourceValue, LetCoercionSemanticContextFlagsBuilder? builder = null)
    {
        var frame = new LetCoercionStackFrame(expression.Identity, operand, source, new VBTypeDescValue(destination));
        builder ??= new LetCoercionSemanticContextFlagsBuilder();
        return new Analysis(BuildProvider().Analyze(null!, builder, expression, frame), builder);
    }
}
