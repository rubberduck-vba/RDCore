using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.SDK.Semantics.Static.Abstract;

/// <summary>
/// The compile-time context an <see cref="IStaticSemantics"/> rule evaluates an expression against:
/// the name-resolution service, and the <see cref="LexicalScope"/> a <c>SimpleName</c> or
/// <c>MemberAccess</c> expression resolves from (<strong>RD-VBAL §2.3.1.2</strong>).
/// </summary>
/// <remarks>
/// Deliberately just these two. A rule that needs a module-level fact — <c>Option Explicit</c>,
/// <c>Option Compare</c>, whatever MS-VBAL directive comes next — reads it off
/// <see cref="LexicalScope.EnclosingModuleDirectives"/> through <see cref="Scope"/>, rather than this
/// record growing a parameter for it: <see cref="ModuleDirectives"/> is where that kind of fact
/// belongs, so this signature never has to change again to carry one.
/// </remarks>
/// <param name="Resolver">Resolves identifier names to symbols and reads their bound values.</param>
/// <param name="Scope">The scope the expression being evaluated is lexically found in.</param>
public readonly record struct StaticEvaluationContext(ISymbolResolver Resolver, LexicalScope Scope);
