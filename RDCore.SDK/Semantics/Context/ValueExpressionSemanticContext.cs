using RDCore.SDK.Semantics.Context.Abstract;
using RDCore.SDK.Semantics.Flags;
using System.Collections.Immutable;

namespace RDCore.SDK.Semantics.Context;

public record class ValueExpressionSemanticContext : SemanticContext<ValueExpressionSemanticFlags>
{
    public ImmutableDictionary<Type, int> TokenSemanticFlags { get; init; } = [];
}
