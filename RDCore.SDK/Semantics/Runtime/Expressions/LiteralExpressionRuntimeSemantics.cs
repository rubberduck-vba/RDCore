using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Runtime;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Semantics.Runtime.Abstract;
using RDCore.SDK.Semantics.Runtime.Operators.Context;
using System.Collections.Immutable;

namespace RDCore.SDK.Semantics.Runtime.Expressions
{
    [Flags]
    public enum ValueExpressionSemanticFlags
    {
        // anything here?
        // ...language core being anemic here is probably fine.
    }

    public record class ValueExpressionSemanticContext : SemanticContext<ValueExpressionSemanticFlags>
    {
        public ImmutableDictionary<Type, int> TokenSemanticFlags { get; init; } = [];
    }

    public record class NumberLiteralTokenSemanticContext : SemanticContext<NumberTokenSemanticFlags> { }
    public record class DateLiteralTokenSemanticContext : SemanticContext<DateTokenSemanticFlags> { }
    public record class StringLiteralTokenSemanticContext : SemanticContext<StringTokenSemanticFlags> { }
    public record class IdentifierLiteralTokenSemanticContext : SemanticContext<IdentifierTokenSemanticFlags> { }


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
}
