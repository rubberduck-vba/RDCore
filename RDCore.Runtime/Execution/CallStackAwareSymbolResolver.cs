using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using System.Diagnostics.CodeAnalysis;

namespace RDCore.Runtime.Execution;

/// <summary>
/// Layers the <em>local stack frame</em> heap tier over an inner <see cref="ISymbolResolver"/>
/// (<strong>RD-VBAL §2.3.1.2</strong>'s lookup order): a locally-scoped symbol's value is read from the
/// current <see cref="ICallStack"/> frame when one is active and declares it, falling back to
/// <paramref name="inner"/> (the session-wide module/global bindings) otherwise. Name resolution
/// (<see cref="ResolveValue"/>) is untouched — a local already resolves first through the scope tree a
/// procedure's <see cref="LexicalScope"/> walk naturally visits before its enclosing module.
/// </summary>
/// <remarks>
/// <see cref="TryRead"/> needs no frame-awareness: every table (session-wide or per-frame) reserves
/// its addresses through the same shared <see cref="ISessionStorage"/>, so an address is already
/// globally unique and readable without knowing which table registered it.
/// </remarks>
/// <param name="callStack">The session's call stack.</param>
/// <param name="inner">The session-wide resolver — module/global bindings.</param>
/// <param name="instances">
/// Where the session's live objects are found, for a field of a class: it lives on the object the current activation
/// is a call on. A resolver with none resolves no instance symbol, which is what it did before there was one.
/// </param>
public sealed class CallStackAwareSymbolResolver(ICallStack callStack, ISymbolResolver inner, ISessionSymbols? instances = null) : ISymbolResolver
{
    // a field of a class is not the session's: it is the storage of the object the current activation is a call on.
    private bool TryInstanceOf(Symbol symbol, [NotNullWhen(true)] out IObjectInstance? instance)
    {
        instance = null;
        return symbol.ScopeKind is ScopeKind.Instance
            && callStack.Current?.Target is { } target
            && instances is not null
            && instances.TryGetInstance(target, out instance);
    }

    /// <inheritdoc/>
    public SymbolResolutionResult ResolveValue(string name, ScopeKind scope, Uri handle) => inner.ResolveValue(name, scope, handle);

    /// <inheritdoc/>
    public SymbolResolutionResult ResolveType(string name, ScopeKind scope, Uri handle) => inner.ResolveType(name, scope, handle);

    /// <inheritdoc/>
    public SymbolResolutionResult ResolveQualifier(string name, ScopeKind scope, Uri handle) => inner.ResolveQualifier(name, scope, handle);

    /// <inheritdoc/>
    public SymbolResolutionResult ResolveConditionalConstant(string name, ScopeKind scope, Uri handle)
        => inner.ResolveConditionalConstant(name, scope, handle);

    /// <inheritdoc/>
    public SymbolResolutionResult ResolveMember(Symbol owner, string name, Uri handle) => inner.ResolveMember(owner, name, handle);

    /// <inheritdoc/>
    public IBindingHandle GetValue(Symbol symbol)
        => symbol.ScopeKind is ScopeKind.Local && callStack.Current is { } frame && frame.TryResolve(symbol, out var local)
            ? local
            : TryInstanceOf(symbol, out var instance) && instance.TryResolve(symbol, out var field)
                ? field
                : inner.GetValue(symbol);

    /// <inheritdoc/>
    public bool TryRead(MemoryAddress address, [NotNullWhen(true)][MaybeNullWhen(false)] out IBindingHandle? value)
        => inner.TryRead(address, out value);

    /// <inheritdoc/>
    public bool TryGetAddress(Symbol symbol, out MemoryAddress address)
    {
        if (symbol.ScopeKind is ScopeKind.Local && callStack.Current is { } frame && frame.TryGetAddress(symbol, out address))
        {
            return true;
        }

        if (TryInstanceOf(symbol, out var instance) && instance.TryGetAddress(symbol, out address))
        {
            return true;
        }

        return inner.TryGetAddress(symbol, out address);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Allocating NEW storage is a session-level concern (a <c>Static</c> local's own first-call allocation,
    /// chiefly), which is <paramref name="inner"/>'s. A variable the current activation or the object it is a call on
    /// already holds is not new storage: it is what a <c>ReDim</c> gives another array, which the variable's own binding
    /// then holds, where everything that reads the variable looks for it.
    /// </remarks>
    public bool TryAllocate(Symbol symbol, VBTypedValue value, out MemoryAddress address)
    {
        address = default;
        IBindingHandle? held = null;
        if (symbol.ScopeKind is ScopeKind.Local && callStack.Current is { } frame && frame.TryResolve(symbol, out var local))
        {
            held = local;
            frame.TryGetAddress(symbol, out address);
        }
        else if (TryInstanceOf(symbol, out var instance) && instance.TryResolve(symbol, out var field))
        {
            held = field;
            instance.TryGetAddress(symbol, out address);
        }

        if (held is null)
        {
            return inner.TryAllocate(symbol, value, out address);
        }

        if (!held.BindingCapabilities.HasFlag(BindingCapabilities.SetValue))
        {
            return false;
        }

        held.SetValue(this, SymbolAddressTable.BoxedValue(value));
        return true;
    }
}
