using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
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
/// (<see cref="Resolve"/>) is untouched — a local already resolves first through the scope tree a
/// procedure's <see cref="LexicalScope"/> walk naturally visits before its enclosing module.
/// </summary>
/// <remarks>
/// <see cref="TryRead"/> needs no frame-awareness: every table (session-wide or per-frame) reserves
/// its addresses through the same shared <see cref="ISessionStorage"/>, so an address is already
/// globally unique and readable without knowing which table registered it.
/// </remarks>
/// <param name="callStack">The session's call stack.</param>
/// <param name="inner">The session-wide resolver — module/global bindings.</param>
public sealed class CallStackAwareSymbolResolver(ICallStack callStack, ISymbolResolver inner) : ISymbolResolver
{
    /// <inheritdoc/>
    public SymbolResolutionResult Resolve(string name, ScopeKind scope, Uri handle) => inner.Resolve(name, scope, handle);

    /// <inheritdoc/>
    public IBindingHandle GetValue(Symbol symbol)
        => symbol.ScopeKind is ScopeKind.Local && callStack.Current is { } frame && frame.TryResolve(symbol, out var local)
            ? local
            : inner.GetValue(symbol);

    /// <inheritdoc/>
    public bool TryRead(MemoryAddress address, [NotNullWhen(true)][MaybeNullWhen(false)] out IBindingHandle? value)
        => inner.TryRead(address, out value);
}
