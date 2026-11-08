using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.SDK.Runtime.Abstract.Execution;

/// <summary>
/// A service that resolves an <em>identifier name</em> to a <c>Symbol</c> given a <em>scope</em> <c>Uri</c>
/// (<strong>MS-VBAL §5.6.4</strong>, <strong>§5.6.10</strong>).
/// </summary>
/// <remarks>
/// Name lookup only: what a name binds to, never what is stored for it. The storage a running session binds its symbols to is
/// <see cref="ISymbolStorage"/>, and a resolver of a running session is both (<see cref="IRuntimeSymbolResolver"/>). The static front end and the
/// language server resolve names with no session at all, so they need nothing more than this.
/// </remarks>
public interface ISymbolResolver
{
    /// <summary>
    /// Resolves the specified <em>identifier name</em> in the <em>default binding context</em>
    /// (<strong>MS-VBAL §5.6.4</strong>), as seen from the scope the symbol at <paramref name="handle"/>
    /// belongs to. This is the context of a simple name expression: it binds a variable, constant,
    /// Enum type or member, property, function, subroutine, procedural module or project
    /// (<strong>§5.6.10</strong>), and never a user-defined type or a class module, which are only ever
    /// candidates in the type binding context (<see cref="ResolveType"/>). A class module is a name here
    /// only through its predeclared instance (<strong>§5.2.4.1.2</strong>): a variable named after the class.
    /// </summary>
    /// <param name="name">The name of the <see cref="Symbol"/> to resolve.</param>
    /// <param name="scope">A memory-scope hint; the compile-time resolver does not consult it.</param>
    /// <param name="handle">The <see cref="Uri"/> of the symbol the lookup originates from.</param>
    /// <returns>
    /// A <see cref="SymbolResolutionResult"/>: the bound <see cref="Symbol"/>, an unbound result, or
    /// a <see cref="Model.Errors.VBCompileErrorId.DuplicateDeclaration"/> /
    /// <see cref="Model.Errors.VBCompileErrorId.AmbiguousName"/> error with the colliding candidates.
    /// </returns>
    SymbolResolutionResult ResolveValue(string name, ScopeKind scope, Uri handle);

    /// <summary>
    /// Resolves the specified <em>identifier name</em> in the <em>type binding context</em>
    /// (<strong>MS-VBAL §5.6.4</strong>), as seen from the scope the symbol at <paramref name="handle"/>
    /// belongs to. This is the context of an expression that expects a type or class name: an
    /// <c>As</c> clause, the operand of <c>New</c>: it binds a user-defined type, an Enum type, a class or
    /// procedural module, or the project. A local, parameter, constant, variable or procedure is never a
    /// candidate, however it is named, so it can neither be bound nor hide the type it shadows in
    /// <see cref="ResolveValue"/>. The qualifier of a qualified type name (<c>A</c> in <c>A.B</c>) is bound by
    /// <see cref="ResolveQualifier"/> instead.
    /// </summary>
    /// <param name="name">The name of the <see cref="Symbol"/> to resolve.</param>
    /// <param name="scope">A memory-scope hint; the compile-time resolver does not consult it.</param>
    /// <param name="handle">The <see cref="Uri"/> of the symbol the lookup originates from.</param>
    /// <returns>
    /// A <see cref="SymbolResolutionResult"/>: the bound <see cref="Symbol"/>, an unbound result, or
    /// a <see cref="Model.Errors.VBCompileErrorId.DuplicateDeclaration"/> /
    /// <see cref="Model.Errors.VBCompileErrorId.AmbiguousName"/> error with the colliding candidates.
    /// </returns>
    SymbolResolutionResult ResolveType(string name, ScopeKind scope, Uri handle);

    /// <summary>
    /// Resolves the specified <em>identifier name</em> as the <em>qualifier</em> of a qualified type name (the
    /// <c>A</c> in <c>A.B</c>) in the <em>type binding context</em> (<strong>MS-VBAL §5.6.4</strong>), as seen from
    /// the scope the symbol at <paramref name="handle"/> belongs to. A qualifier is a namespace: it binds the project,
    /// or a procedural or class module, and never a user-defined type or an Enum type, however the name ranks in
    /// <see cref="ResolveType"/>: neither can contain a type. A local, parameter, constant, variable or procedure is
    /// never a candidate either.
    /// </summary>
    /// <remarks>
    /// Which lookup a name uses is positional: the last part of <c>A.B</c>, like a bare name, is bound by
    /// <see cref="ResolveType"/>; only what precedes a dot is bound here. This holds wherever a type name appears:
    /// an <c>As</c> clause, <c>As New</c>, the operand of <c>New</c>.
    /// </remarks>
    /// <param name="name">The name of the <see cref="Symbol"/> to resolve.</param>
    /// <param name="scope">A memory-scope hint; the compile-time resolver does not consult it.</param>
    /// <param name="handle">The <see cref="Uri"/> of the symbol the lookup originates from.</param>
    /// <returns>
    /// A <see cref="SymbolResolutionResult"/>: the bound <see cref="Symbol"/>, an unbound result, or
    /// an <see cref="Model.Errors.VBCompileErrorId.AmbiguousName"/> error with the colliding candidates.
    /// </returns>
    SymbolResolutionResult ResolveQualifier(string name, ScopeKind scope, Uri handle);

    /// <summary>
    /// Resolves the type <paramref name="name"/> that a <em>project</em> declares: the last part of <c>Project.Name</c>
    /// (<strong>MS-VBAL §5.6.12</strong>).
    /// </summary>
    /// <remarks>
    /// The project tier is shared by the enclosing project and every library the project references, and the types of two of them can be of one name - a host
    /// library and the library of its editor both have an <c>Application</c>. What the qualifier says is which of the candidates is meant, so the candidates are
    /// those of the project and no others <em>before</em> it is decided that the name is ambiguous. A project is told from another by the library its symbols
    /// say they are of (<see cref="SymbolProperties.Library"/>), which the enclosing project's own are not of any.
    /// <para>
    /// The default asks <see cref="ResolveType"/> and accepts what it gives if it is the project's, which is right for a resolver that has one candidate of a
    /// name to give; one that decides ambiguity itself overrides it.
    /// </para>
    /// </remarks>
    /// <param name="project">The project, as <see cref="ResolveQualifier"/> bound it.</param>
    /// <param name="name">The name of the type.</param>
    SymbolResolutionResult ResolveProjectType(VBProjectSymbol project, string name)
    {
        var found = ResolveType(name, ScopeKind.Global, project.WorkspaceRoot);
        return found.Symbol is { } type
            && string.Equals(type.GetProperty(SymbolProperties.Library) ?? string.Empty, project.GetProperty(SymbolProperties.Library) ?? string.Empty, StringComparison.Ordinal)
                ? found
                : SymbolResolutionResult.Unbound;
    }

    /// <summary>
    /// Resolves the specified <em>identifier name</em> as a reference to a <em>conditional compilation
    /// constant</em>, as seen from the scope the symbol at <paramref name="handle"/> belongs to.
    /// </summary>
    /// <remarks>
    /// Its own binding context, and the reason is <strong>MS-VBAL §3.4.1</strong>: a <c>#Const</c> "defines
    /// a constant binding <em>accessible to &lt;cc-expression&gt; elements</em> of the containing module",
    /// and <strong>§5.6.16.2</strong> is the only place a reference to one is defined at all. So a
    /// conditional compilation constant is <em>not</em> a name in the default binding context (<c>x = Win64</c>
    /// in ordinary source does not bind it, and <see cref="ResolveValue"/> never returns one), while a
    /// <c>#If Win64 Then</c> resolves it only here.
    /// <para>
    /// §3.4.1 also gives the shadowing rule this follows: a module's own <c>#Const</c> shadows a
    /// project-level constant of the same name.
    /// </para>
    /// </remarks>
    /// <param name="name">The name of the constant to resolve.</param>
    /// <param name="scope">A memory-scope hint; the compile-time resolver does not consult it.</param>
    /// <param name="handle">The <see cref="Uri"/> of the symbol the lookup originates from.</param>
    /// <returns>
    /// A <see cref="SymbolResolutionResult"/> carrying the bound constant, or an unbound result, which
    /// <strong>§5.6.16.2</strong> gives a meaning of its own: a conditional compilation constant that
    /// names nothing evaluates to <c>0</c>, not to an error.
    /// </returns>
    SymbolResolutionResult ResolveConditionalConstant(string name, ScopeKind scope, Uri handle);

    /// <summary>
    /// Resolves <paramref name="name"/> as a member of <paramref name="owner"/>, a project or a procedural module: the
    /// right-hand side of a member access whose left-hand side is one of them (<strong>MS-VBAL §5.6.12</strong>):
    /// <c>Strings.LenB</c>, <c>VBA.Strings</c>, <c>VBA.LenB</c>.
    /// </summary>
    /// <remarks>
    /// What the member of a <em>procedural module</em> can be is what the module declares and the lookup can reach:
    /// a variable, property, function, subroutine or value, and not one declared <c>Private</c> unless the lookup
    /// originates in the module itself.
    /// <para>
    /// What the member of a <em>project</em> can be is, in the order the specification gives them: a project, when
    /// <paramref name="owner"/> is the enclosing project; a procedural module of the project of that name; and, only
    /// when there is no such module, the one accessible member of that name that exactly one of the project's
    /// procedural modules has: a name two of them declare is ambiguous, and the reference has to qualify it.
    /// </para>
    /// <para>
    /// 👉 A class module is not an owner here: its members are reached through an instance of it, which is a member
    /// access on a value and not on a namespace.
    /// </para>
    /// </remarks>
    /// <param name="owner">The project or procedural module the member belongs to.</param>
    /// <param name="name">The name of the member to resolve.</param>
    /// <param name="handle">The <see cref="Uri"/> of the symbol the lookup originates from, which decides what is accessible.</param>
    /// <returns>
    /// A <see cref="SymbolResolutionResult"/> carrying the member, an unbound result when there is none, or an
    /// <see cref="Model.Errors.VBCompileErrorId.AmbiguousName"/> error with the colliding candidates.
    /// </returns>
    SymbolResolutionResult ResolveMember(Symbol owner, string name, Uri handle);
}

