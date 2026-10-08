using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.SDK.Semantics;

/// <summary>
/// Recognizing the expressions that name a project or a procedural module rather than a value
/// (<strong>MS-VBAL §5.6.12</strong>): the left-hand side of <c>Strings.LenB</c>, and of <c>VBA.Strings.LenB</c>.
/// </summary>
/// <remarks>
/// Such an expression is a namespace. It has no value to evaluate and no declared type, and a member access on it
/// resolves the member in the namespace — <see cref="ISymbolResolver.ResolveMember"/> — instead of looking it up among
/// the members of a value's type. Which of the two a left-hand side is has to be settled by what it resolves to,
/// from the scope it is written in, and the answer is the same whether the question is asked while compiling the
/// expression or while running it, so it is answered here once.
/// </remarks>
public static class NamespaceExpressions
{
    /// <summary>
    /// The project or procedural module <paramref name="expression"/> names, when it names one.
    /// </summary>
    /// <remarks>
    /// Classification is by what the name resolves to, so whatever is nearer than a module of that name — a local, a
    /// parameter, a variable of the module — is what the name means, as it is anywhere else. A member access whose own
    /// left-hand side is a namespace is one too, when its member is a project or a module: <c>VBA.Strings</c> in
    /// <c>VBA.Strings.LenB</c>.
    /// </remarks>
    /// <param name="resolver">The resolver names are bound by.</param>
    /// <param name="expression">The left-hand side of a member access.</param>
    /// <param name="scope">The <see cref="Uri"/> of the symbol the expression is written in.</param>
    /// <returns>The <see cref="VBProjectSymbol"/> or <see cref="VBStandardModuleSymbol"/> it names, or <see langword="null"/>.</returns>
    public static Symbol? NamespaceOf(this ISymbolResolver resolver, ExpressionNode expression, Uri scope)
    {
        switch (expression)
        {
            case SimpleNameExpressionNode name:
                return resolver.ResolveValue(name, ScopeKind.Local, scope).Symbol is { } named && IsNamespace(named)
                    ? named
                    : null;

            case MemberAccessExpressionNode { Owner: { } owner } access when resolver.NamespaceOf(owner, scope) is { } parent:
                return resolver.ResolveMember(parent, access.Member, scope).Symbol is { } member && IsNamespace(member)
                    ? member
                    : null;

            default:
                return null;
        }
    }

    /// <summary>
    /// Whether <paramref name="symbol"/> is a namespace: a project, or a procedural module.
    /// </summary>
    /// <remarks>A class module is not one: its members are reached through an instance of it.</remarks>
    /// <param name="symbol">The symbol to classify.</param>
    public static bool IsNamespace(Symbol symbol) => symbol is VBProjectSymbol or VBStandardModuleSymbol;
}
