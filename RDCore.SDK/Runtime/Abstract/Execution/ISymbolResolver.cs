using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
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
    /// <c>As</c> clause: it binds a user-defined type, an Enum type, a class or procedural module, or the
    /// project. A local, parameter, constant, variable or procedure is never a candidate, however it is
    /// named, so it can neither be bound nor hide the type it shadows in <see cref="ResolveValue"/>. The
    /// operand of <c>New</c> and of an <c>As New</c> clause is bound by <see cref="ResolveClass"/> instead.
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
    /// Resolves the specified <em>identifier name</em> in the <em>type binding context</em> as narrowed by
    /// <c>New</c> (<strong>MS-VBAL §5.6.8</strong>, <strong>§5.2.3.1.4</strong>), as seen from the scope the symbol
    /// at <paramref name="handle"/> belongs to. This is the context of the operand of a <c>New</c> expression and
    /// of an <c>As New</c> clause: <c>New</c> instantiates classes, so it never looks for a user-defined type or an
    /// Enum type — none is a candidate, however it ranks in <see cref="ResolveType"/> — and what it binds is a
    /// class module, or the project or module that qualifies one (<c>New Project.ClassName</c>). A local,
    /// parameter, constant, variable or procedure is never a candidate either.
    /// </summary>
    /// <remarks>
    /// Whether the class found can actually be instantiated (its <c>VB_Creatable</c> attribute) is not a matter of
    /// name lookup: it is checked once the name is bound, so an existing but non-creatable class is reported as
    /// such rather than as an undefined type.
    /// </remarks>
    /// <param name="name">The name of the <see cref="Symbol"/> to resolve.</param>
    /// <param name="scope">A memory-scope hint; the compile-time resolver does not consult it.</param>
    /// <param name="handle">The <see cref="Uri"/> of the symbol the lookup originates from.</param>
    /// <returns>
    /// A <see cref="SymbolResolutionResult"/> — the bound <see cref="Symbol"/>, an unbound result, or
    /// an <see cref="Model.Errors.VBCompileErrorId.AmbiguousName"/> error with the colliding candidates.
    /// </returns>
    SymbolResolutionResult ResolveClass(string name, ScopeKind scope, Uri handle);

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
}

