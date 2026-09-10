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
    /// Resolves the specified <em>identifier name</em> as seen from the scope the symbol at
    /// <paramref name="handle"/> belongs to.
    /// </summary>
    /// <param name="name">The name of the <see cref="Symbol"/> to resolve.</param>
    /// <param name="scope">A memory-scope hint; the compile-time resolver does not consult it.</param>
    /// <param name="handle">The <see cref="Uri"/> of the symbol the lookup originates from.</param>
    /// <returns>
    /// A <see cref="SymbolResolutionResult"/> — the bound <see cref="Symbol"/>, an unbound result, or
    /// a <see cref="Model.Errors.VBCompileErrorId.DuplicateDeclaration"/> /
    /// <see cref="Model.Errors.VBCompileErrorId.AmbiguousName"/> error with the colliding candidates.
    /// </returns>
    SymbolResolutionResult Resolve(string name, ScopeKind scope, Uri handle);

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

