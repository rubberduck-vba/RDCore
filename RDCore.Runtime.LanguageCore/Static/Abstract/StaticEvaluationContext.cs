using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics;

namespace RDCore.Runtime.LanguageCore.Static.Abstract;

// RD-VBAL §2.3.1.2; MS-VBAL §5.6.15
// deliberately just Resolver and Scope: a module-level fact (Option Explicit, Option Compare) is read off Scope's EnclosingModuleDirectives, rather than growing a parameter.
// EnclosingWithTargetType is threaded through: it is positional in the statement tree, not derivable from Scope.
public readonly record struct StaticEvaluationContext(
    ISymbolResolver Resolver, LexicalScope Scope, VBType? EnclosingWithTargetType = null, IExpressionFactSink? Facts = null,
    Binding.ExpressionBindings? Bindings = null, StaticEnvironment? Environment = null)
{
    // what the expressions of the tree being evaluated evaluated to, which the evaluation of the tree gives every evaluation inside it. It is no part of what
    // the context is: two contexts that differ only by it are equal.
    internal ExpressionEvaluations? Evaluations { get; init; }

    // where the static pass of a module records the parameter each argument it marks is given (ExpressionStaticSemanticsEvaluator.MarkPassedAsArguments).
    // Like the evaluations, it is no part of what the context is.
    public ArgumentParameters? Arguments { get; init; }

    public bool Equals(StaticEvaluationContext other)
        => EqualityComparer<ISymbolResolver>.Default.Equals(Resolver, other.Resolver)
            && EqualityComparer<LexicalScope>.Default.Equals(Scope, other.Scope)
            && EqualityComparer<VBType?>.Default.Equals(EnclosingWithTargetType, other.EnclosingWithTargetType)
            && EqualityComparer<IExpressionFactSink?>.Default.Equals(Facts, other.Facts)
            && EqualityComparer<Binding.ExpressionBindings?>.Default.Equals(Bindings, other.Bindings)
            && EqualityComparer<StaticEnvironment?>.Default.Equals(Environment, other.Environment);

    public override int GetHashCode() => HashCode.Combine(Resolver, Scope, EnclosingWithTargetType, Facts, Bindings, Environment);
}
