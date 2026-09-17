using RDCore.Runtime.Semantics.Abstract;
using RDCore.Runtime.Semantics.Literals;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Context;
using RDCore.SDK.Semantics.Context.Abstract;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Runtime.Semantics.Expressions;

/// <summary>
/// <strong>MS-VBAL 5.6.11</strong> Instance Expressions (runtime semantics). <c>Me</c> resolves like
/// any other parameter — nothing special-cased here: the implicit "Me" parameter
/// <c>SymbolBuilder.BuildParameters</c> synthesizes at slot 0 of every class-module member flows in
/// as a <see cref="VBSymbolDescValue"/> (the same meta-value convention
/// <c>NewExpressionRuntimeSemantics</c> and <c>BinaryLetAssignmentOperatorRuntimeSemantics</c> use for
/// a target symbol resolved ahead of evaluation), and this simply reads its current binding.
/// </summary>
/// <remarks>
/// Actually <em>binding</em> that parameter to the live object instance is a procedure-invocation
/// concern (pushing an activation frame) that doesn't exist yet — evaluating this before anything has
/// pushed a binding for the given symbol throws, same as reading any other unbound local.
/// </remarks>
public sealed record class InstanceExpressionRuntimeSemantics : RuntimeSemantics<ValueExpressionSemanticContext, ValueExpressionSemanticFlags>
{
    private static readonly Lazy<InstanceExpressionRuntimeSemantics> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static InstanceExpressionRuntimeSemantics Instance => _instance.Value;

    public override ISemanticFlagsAccumulator<ValueExpressionSemanticFlags> Analyze(
        IRuntimeSession session,
        ConversionOperationSemanticContext conversionContext,
        ISemanticFlagsAccumulator<ValueExpressionSemanticFlags> builder,
        SyntaxNode node,
        params VBTypedValue[] inputs) => builder;

    public override RuntimeSemanticsEvaluationResult Evaluate(
        IRuntimeSession session,
        ValueExpressionSemanticContext context,
        SyntaxNode node,
        params VBTypedValue[] inputs)
    {
        if (inputs is not [VBSymbolDescValue { Symbol: { } meSymbol }])
        {
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        return RuntimeSemanticsEvaluationResult.Success(new VBObjectValue(session.Symbols.Resolver.GetValue(meSymbol)));
    }
}
