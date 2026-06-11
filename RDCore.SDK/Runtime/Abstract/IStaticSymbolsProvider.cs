using RDCore.SDK.Model;
using RDCore.SDK.Model.Symbols.Abstract;
using System.Diagnostics.CodeAnalysis;

namespace RDCore.SDK.Runtime.Abstract;

/// <summary>
/// A service that provides <em>global scope</em> <see cref="StaticSymbol"/> references.
/// </summary>
public interface IStaticSymbolsProvider
{
    /// <summary>
    /// Gets all symbols defined in this <see cref="IStaticSymbolsProvider"/>.
    /// </summary>
    /// <returns>An <see cref="IEnumerable{T}"/> enumerating <see cref="StaticSymbol"/> elements.</returns>
    IEnumerable<StaticSymbol> GetAll();
    /// <summary>
    /// Retrieves a <see cref="StaticSymbol"/> by its name (identifier token).
    /// </summary>
    /// <param name="name">The name of the <see cref="StaticSymbol"/> to resolve.</param>
    /// <param name="symbol">The resolved <see cref="StaticSymbol"/>, if resolution is successful.</param>
    /// <returns><c>true</c> if the specified <c>name</c> resolves to a specific <see cref="StaticSymbol"/>, <c>false</c> otherwise.</returns>
    bool TryGetByName(string name, [NotNullWhen(true)][MaybeNullWhen(false)] out StaticSymbol symbol);
}
