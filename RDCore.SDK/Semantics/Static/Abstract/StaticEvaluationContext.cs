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
public readonly record struct StaticEvaluationContext(ISymbolResolver Resolver, LexicalScope Scope, VBType? EnclosingWithTargetType = null);
