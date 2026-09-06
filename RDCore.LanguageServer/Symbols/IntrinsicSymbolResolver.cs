using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using System.Diagnostics.CodeAnalysis;

namespace RDCore.LanguageServer.Symbols;

/// <summary>
/// The language server's fallback <see cref="ISymbolResolver"/> until it can resolve project and
/// library symbols: binds the MS-VBAL reserved data-type names and type-declaration characters to
/// their intrinsic <c>VBType</c> (via <see cref="IntrinsicVBTypes"/>), and resolves nothing else.
/// </summary>
/// <remarks>
/// A compile-time type resolver only — it holds no runtime bindings, so the value-lookup members
/// throw. With it in place the <c>SymbolBuilder</c> binds intrinsic declared types instead of
/// leaving every one <c>VBUnknownType</c>; project and library type names still fall through until a
/// resolver composed with those symbols is available.
/// </remarks>
internal sealed class IntrinsicSymbolResolver : ISymbolResolver
{
    /// <inheritdoc/>
    public Symbol? Resolve(string name, ScopeKind scope, Uri handle)
    {
        if (IntrinsicVBTypes.TryResolve(name, out var type) || IntrinsicVBTypes.TryResolveTypeHint(name, out type))
        {
            return new StaticSymbol(name, SymbolKindExt.TypeDescriptor, type);
        }

        return null;
    }

    /// <inheritdoc/>
    public IBindingHandle GetValue(Symbol symbol)
        => throw new NotSupportedException("The intrinsic symbol resolver resolves type names only; it holds no runtime bindings.");

    /// <inheritdoc/>
    public bool TryRead(MemoryAddress address, [NotNullWhen(true)][MaybeNullWhen(false)] out IBindingHandle? value)
    {
        value = null;
        return false;
    }
}
