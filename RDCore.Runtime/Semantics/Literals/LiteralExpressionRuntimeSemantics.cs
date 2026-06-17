using RDCore.Runtime.Semantics.Abstract;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Context;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Runtime.Semantics.Literals;

public record class LiteralExpressionRuntimeSemantics : LiteralValueRuntimeSemantics<ValueExpressionSemanticContext, ValueExpressionSemanticFlags>
{
    private static readonly Lazy<LiteralExpressionRuntimeSemantics> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static LiteralExpressionRuntimeSemantics Instance => _instance.Value;

    public override ISemanticFlagsAccumulator<ValueExpressionSemanticFlags> Analyze(
        ISymbolResolver resolver, 
        ConversionOperationSemanticContext conversionContext, 
        ISemanticFlagsAccumulator<ValueExpressionSemanticFlags> builder, 
        BoundNode node, 
        params VBTypedValue[] inputs) => builder;
}
