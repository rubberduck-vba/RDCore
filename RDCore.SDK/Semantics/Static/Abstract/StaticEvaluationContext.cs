using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.SDK.Semantics.Static.Abstract;

/// <summary>
/// The compile-time context an <see cref="IStaticSemantics"/> rule evaluates an expression against:
/// the name-resolution service, the <see cref="LexicalScope"/> a <c>SimpleName</c> or
/// <c>MemberAccess</c> expression resolves from (<strong>RD-VBAL §2.3.1.2</strong>), and the innermost
/// enclosing <c>With</c> block's target type, if any.
/// </summary>
/// <remarks>
/// <see cref="Resolver"/> and <see cref="Scope"/> are deliberately just those two: a rule that needs a
/// module-level fact — <c>Option Explicit</c>, <c>Option Compare</c>, whatever MS-VBAL directive comes
/// next — reads it off <see cref="LexicalScope.EnclosingModuleDirectives"/> through <see cref="Scope"/>,
/// rather than this record growing a parameter for it. <see cref="EnclosingWithTargetType"/> is a
/// different kind of fact — it isn't lexical (derivable from <see cref="Scope"/> alone), it's
/// statement-tree-positional: only a caller that has walked the enclosing statement tree (see
/// <c>StatementStaticSemanticsEvaluator</c>) knows it, which is why it's threaded through here rather
/// than derived.
/// </remarks>
/// <param name="Resolver">Resolves identifier names to symbols and reads their bound values.</param>
/// <param name="Scope">The scope the expression being evaluated is lexically found in.</param>
/// <param name="EnclosingWithTargetType">
/// The declared type of the innermost enclosing <c>With</c> block's target expression
/// (<strong>MS-VBAL §5.6.15</strong>), or <c>null</c> when the expression being evaluated is not
/// inside any <c>With</c> block. A with-relative access (<c>.Member</c>/<c>!Member</c>) resolves
/// against this; per §5.6.15, one is invalid when this is <c>null</c>.
/// </param>
/// <param name="Facts">
/// Where the facts of the expressions evaluated are recorded (<see cref="ExpressionFact"/>), or <c>null</c> when the caller
/// has no use for anything but the declared type.
/// </param>
/// <param name="Bindings">
/// Where what each expression evaluated binds to is recorded - its declared type, and the plan of an operation (<see cref="Binding.ExpressionBinder"/>) -
/// or <c>null</c> when the expressions are not being bound.
/// </param>
/// <param name="Environment">
/// What the environment the code is compiled for decides (<see cref="StaticEnvironment"/>), or <c>null</c> when no environment is stated: the type of what
/// only it decides - the address <c>AddressOf</c> gives - is then not known.
/// </param>
public readonly record struct StaticEvaluationContext(
    ISymbolResolver Resolver, LexicalScope Scope, VBType? EnclosingWithTargetType = null, IExpressionFactSink? Facts = null,
    Binding.ExpressionBindings? Bindings = null, StaticEnvironment? Environment = null)
{
    // what the expressions of the tree being evaluated evaluated to, which the evaluation of the tree gives every evaluation inside it. It is no part of what
    // the context is: two contexts that differ only by it are equal.
    internal ExpressionEvaluations? Evaluations { get; init; }

    /// <summary>
    /// Whether <paramref name="other"/> is the same context: the same resolver, scope, <c>With</c> target, sinks and environment.
    /// </summary>
    /// <param name="other">The context to compare with.</param>
    public bool Equals(StaticEvaluationContext other)
        => EqualityComparer<ISymbolResolver>.Default.Equals(Resolver, other.Resolver)
            && EqualityComparer<LexicalScope>.Default.Equals(Scope, other.Scope)
            && EqualityComparer<VBType?>.Default.Equals(EnclosingWithTargetType, other.EnclosingWithTargetType)
            && EqualityComparer<IExpressionFactSink?>.Default.Equals(Facts, other.Facts)
            && EqualityComparer<Binding.ExpressionBindings?>.Default.Equals(Bindings, other.Bindings)
            && EqualityComparer<StaticEnvironment?>.Default.Equals(Environment, other.Environment);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Resolver, Scope, EnclosingWithTargetType, Facts, Bindings, Environment);
}
