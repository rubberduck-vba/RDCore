using RDCore.SDK.Semantics.Context.Abstract;
using RDCore.SDK.Semantics.Flags;
using System.Collections.Immutable;

namespace RDCore.Runtime.Semantics.Literals;

public record class ValueExpressionSemanticContext : SemanticContext<ValueExpressionSemanticFlags>
{
    public ImmutableDictionary<Type, int> TokenSemanticFlags { get; init; } = [];
}
