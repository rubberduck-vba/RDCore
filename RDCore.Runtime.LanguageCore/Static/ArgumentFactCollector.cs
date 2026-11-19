using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Semantics;
using System.Collections.Immutable;

namespace RDCore.Runtime.LanguageCore.Static;

// MS-VBAL §5.3.1.11: the parameter each argument of a procedure's calls is mapped to, in the order the static pass meets them. An argument evaluated twice is
// mapped once.
public sealed class ArgumentFactCollector
{
    private readonly Dictionary<SyntaxNodeId, ArgumentFact> _facts = [];
    private readonly List<ArgumentFact> _ordered = [];

    public void Record(ArgumentFact fact)
    {
        if (_facts.TryAdd(fact.Argument, fact))
        {
            _ordered.Add(fact);
        }
    }

    public ImmutableArray<ArgumentFact> ToImmutable() => [.. _ordered];
}
