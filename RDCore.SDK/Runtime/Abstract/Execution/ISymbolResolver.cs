using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Runtime.Shared;
using System.Diagnostics.CodeAnalysis;

namespace RDCore.SDK.Runtime.Abstract.Execution;

/// <summary>
/// A service that resolves an <em>identifier name</em> to a <c>Symbol</c> given a <em>scope</em> <c>Uri</c>
/// and that can look up the value currently bound to a symbol (or address) in the current context.
/// </summary>
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
    /// A <see cref="SymbolResolutionResult"/> — the bound <see cref="Symbol"/>, an unbound result, or
    /// a <see cref="Model.Errors.VBCompileErrorId.DuplicateDeclaration"/> /
    /// <see cref="Model.Errors.VBCompileErrorId.AmbiguousName"/> error with the colliding candidates.
    /// </returns>
    SymbolResolutionResult ResolveValue(string name, ScopeKind scope, Uri handle);

    /// <summary>
    /// Resolves the specified <em>identifier name</em> in the <em>type binding context</em>
    /// (<strong>MS-VBAL §5.6.4</strong>), as seen from the scope the symbol at <paramref name="handle"/>
    /// belongs to. This is the context of an expression that expects a type or class name — an
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
    /// A <see cref="SymbolResolutionResult"/> — the bound <see cref="Symbol"/>, an unbound result, or
    /// a <see cref="Model.Errors.VBCompileErrorId.DuplicateDeclaration"/> /
    /// <see cref="Model.Errors.VBCompileErrorId.AmbiguousName"/> error with the colliding candidates.
    /// </returns>
    SymbolResolutionResult ResolveType(string name, ScopeKind scope, Uri handle);

    /// <summary>
    /// Resolves the specified <em>identifier name</em> as the <em>qualifier</em> of a qualified type name — the
    /// <c>A</c> in <c>A.B</c> — in the <em>type binding context</em> (<strong>MS-VBAL §5.6.4</strong>), as seen from
    /// the scope the symbol at <paramref name="handle"/> belongs to. A qualifier is a namespace: it binds the project,
    /// or a procedural or class module, and never a user-defined type or an Enum type, however the name ranks in
    /// <see cref="ResolveType"/> — neither can contain a type. A local, parameter, constant, variable or procedure is
    /// never a candidate either.
    /// </summary>
    /// <remarks>
    /// Which lookup a name uses is positional: the last part of <c>A.B</c>, like a bare name, is bound by
    /// <see cref="ResolveType"/>; only what precedes a dot is bound here. This holds wherever a type name appears —
    /// an <c>As</c> clause, <c>As New</c>, the operand of <c>New</c>.
    /// </remarks>
    /// <param name="name">The name of the <see cref="Symbol"/> to resolve.</param>
    /// <param name="scope">A memory-scope hint; the compile-time resolver does not consult it.</param>
    /// <param name="handle">The <see cref="Uri"/> of the symbol the lookup originates from.</param>
    /// <returns>
    /// A <see cref="SymbolResolutionResult"/> — the bound <see cref="Symbol"/>, an unbound result, or
    /// an <see cref="Model.Errors.VBCompileErrorId.AmbiguousName"/> error with the colliding candidates.
    /// </returns>
    SymbolResolutionResult ResolveQualifier(string name, ScopeKind scope, Uri handle);

    /// <summary>
    /// Resolves the specified <em>identifier name</em> as a reference to a <em>conditional compilation
    /// constant</em>, as seen from the scope the symbol at <paramref name="handle"/> belongs to.
    /// </summary>
    /// <remarks>
    /// Its own binding context, and the reason is <strong>MS-VBAL §3.4.1</strong>: a <c>#Const</c> "defines
    /// a constant binding <em>accessible to &lt;cc-expression&gt; elements</em> of the containing module",
    /// and <strong>§5.6.16.2</strong> is the only place a reference to one is defined at all. So a
    /// conditional compilation constant is <em>not</em> a name in the default binding context — <c>x = Win64</c>
    /// in ordinary source does not bind it, and <see cref="ResolveValue"/> never returns one — while a
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
    /// A <see cref="SymbolResolutionResult"/> carrying the bound constant, or an unbound result — which
    /// <strong>§5.6.16.2</strong> gives a meaning of its own: a conditional compilation constant that
    /// names nothing evaluates to <c>0</c>, not to an error.
    /// </returns>
    SymbolResolutionResult ResolveConditionalConstant(string name, ScopeKind scope, Uri handle);

    /// <summary>
    /// Resolves <paramref name="name"/> as a member of <paramref name="owner"/>, a project or a procedural module: the
    /// right-hand side of a member access whose left-hand side is one of them (<strong>MS-VBAL §5.6.12</strong>) —
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
    /// procedural modules has — a name two of them declare is ambiguous, and the reference has to qualify it.
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

    /// <summary>
    /// Gets the <see cref="IBindingHandle"/> currently associated with the specified <see cref="Symbol"/>.
    /// </summary>
    /// <param name="symbol">The <see cref="Symbol"/> to retrieve the currently associated binding for.</param>
    IBindingHandle GetValue(Symbol symbol);

    /// <summary>
    /// Gets the <see cref="IBindingHandle"/> at the specified address in the runtime <em>memory map</em>.
    /// </summary>
    /// <param name="address">The memory address to read.</param>
    /// <param name="value">The retrieved binding, if successful.</param>
    bool TryRead(MemoryAddress address, [NotNullWhen(true)][MaybeNullWhen(false)] out IBindingHandle? value);

    /// <summary>
    /// Gets the <see cref="MemoryAddress"/> currently reserved for <paramref name="symbol"/>, if any —
    /// what a <c>ByRef</c> argument binds to (<strong>MS-VBAL §5.3.1.11</strong>: "a reference parameter
    /// binding... referring to the variable referenced by the argument's expression").
    /// </summary>
    /// <param name="symbol">The <see cref="Symbol"/> whose address to look up.</param>
    /// <param name="address">The reserved address, if <paramref name="symbol"/> is currently allocated.</param>
    bool TryGetAddress(Symbol symbol, out MemoryAddress address);

    /// <summary>
    /// Reserves storage sized for <paramref name="value"/> and binds it to <paramref name="symbol"/> —
    /// a <c>Static</c> local's own first-call allocation (<strong>MS-VBAL §5.4.3.1</strong>: module
    /// extent, so it must persist past its own frame popping) chiefly. This is a storage-only
    /// operation: it never affects name resolution, so it is never how a symbol's name itself becomes
    /// resolvable — that rides on its own declaring symbol instead (a procedure's own
    /// <c>Locals</c>/<c>Parameters</c>), the same way calling this twice for an already-allocated
    /// symbol is safe (a fresh allocation, the previous one freed first) but never necessary.
    /// </summary>
    /// <param name="symbol">The symbol to reserve storage for.</param>
    /// <param name="value">The value to seed the new storage with.</param>
    /// <param name="address">The reserved address, on success.</param>
    /// <returns><c>false</c> if the underlying memory space is exhausted.</returns>
    bool TryAllocate(Symbol symbol, VBTypedValue value, out MemoryAddress address);
}

