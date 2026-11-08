using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Symbols;

/// <summary>
/// The language server's fallback <see cref="ISymbolResolver"/> until it can resolve project and
/// library symbols: binds the MS-VBAL reserved data-type names and type-declaration characters to
/// their intrinsic <c>VBType</c> (via <see cref="IntrinsicVBTypes"/>), and resolves nothing else.
/// </summary>
/// <remarks>
/// A compile-time type resolver only — it holds no runtime bindings. With it in place the <c>SymbolBuilder</c> binds intrinsic declared types instead of
/// leaving every one <c>VBUnknownType</c>; project and library type names still fall through until a
/// resolver composed with those symbols is available. A reserved data-type name is a type, never a
/// value, so it binds in the type binding context only.
/// </remarks>
/// <param name="is64Bit">Whether the environment is 64-bit: what <c>LongPtr</c> is an alias of.</param>
public sealed class IntrinsicSymbolResolver(bool is64Bit = true) : ISymbolResolver
{
    /// <inheritdoc/>
    public SymbolResolutionResult ResolveValue(string name, ScopeKind scope, Uri handle) => SymbolResolutionResult.Unbound;

    /// <inheritdoc/>
    public SymbolResolutionResult ResolveType(string name, ScopeKind scope, Uri handle)
        => IntrinsicVBTypes.TryResolve(name, is64Bit, out var type) || IntrinsicVBTypes.TryResolveTypeHint(name, out type)
            ? SymbolResolutionResult.Resolved(new StaticSymbol(name, SymbolKindExt.TypeDescriptor, type))
            : SymbolResolutionResult.Unbound;

    /// <inheritdoc/>
    public SymbolResolutionResult ResolveQualifier(string name, ScopeKind scope, Uri handle) => SymbolResolutionResult.Unbound; // a reserved data-type name is no namespace

    /// <inheritdoc/>
    public SymbolResolutionResult ResolveConditionalConstant(string name, ScopeKind scope, Uri handle)
        => SymbolResolutionResult.Unbound; // nor a conditional compilation constant

    /// <inheritdoc/>
    public SymbolResolutionResult ResolveMember(Symbol owner, string name, Uri handle)
        => SymbolResolutionResult.Unbound; // the intrinsic types are no project or module to have members
}
