using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Runtime.Shared;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace RDCore.SDK.Model.Symbols;

/// <summary>
/// The lexical-scope tree of a composed set of symbols: a <see cref="Global"/> scope at the root, a
/// scope per module, and a scope per procedure body. Built by <see cref="ScopeTreeBuilder"/>; the
/// static and run-time name resolvers walk it to bind an identifier
/// (<strong>RD-VBAL §2.3.1.2</strong>).
/// </summary>
public sealed class ScopeTree
{
    // keyed by Uri.AbsoluteUri — Uri equality ignores the fragment, but a symbol's scope path lives
    // entirely in the fragment (workspace#Module.Member).
    private readonly IReadOnlyDictionary<string, LexicalScope> _scopeByUri;

    internal ScopeTree(LexicalScope global, IReadOnlyDictionary<string, LexicalScope> scopeByUri)
    {
        Global = global;
        _scopeByUri = scopeByUri;
    }

    /// <summary>
    /// The root scope — every lookup that is not bound sooner ends here.
    /// </summary>
    public LexicalScope Global { get; }

    /// <summary>
    /// What each name means as a value, where it was looked up from: a name is resolved against this tree once
    /// (<see cref="ScopeTreeSymbolResolver.ResolveValue"/>).
    /// </summary>
    /// <remarks>
    /// The resolution of a name is a function of the tree, which does not change once it is built: a symbol table that changes builds another tree, whose
    /// resolutions are its own, and begin empty. Nothing is ever taken out, and nothing has to be - what a tree resolved is true of that tree. A name is the
    /// same name in any case (MS-VBAL §3.3.5.1), as the tree declares it.
    /// </remarks>
    internal ConcurrentDictionary<ValueResolutionKey, SymbolResolutionResult> ValueResolutions { get; } = new();

    /// <summary>
    /// A name, and where it was looked up from.
    /// </summary>
    internal readonly record struct ValueResolutionKey(string Name, ScopeKind Scope, string Handle)
    {
        public bool Equals(ValueResolutionKey other)
            => Scope == other.Scope && string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase) && string.Equals(Handle, other.Handle, StringComparison.Ordinal);

        public override int GetHashCode() => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(Name), Scope, StringComparer.Ordinal.GetHashCode(Handle));
    }

    /// <summary>
    /// The scope a name lookup should start from when it originates at <paramref name="uri"/>: the
    /// scope <paramref name="uri"/> <em>defines</em> if it is a module or a procedure, otherwise the
    /// scope that <em>declares</em> the symbol at <paramref name="uri"/>. Falls back to
    /// <see cref="Global"/> for a uri the tree does not know.
    /// </summary>
    public LexicalScope ScopeFor(Uri uri)
        => _scopeByUri.TryGetValue(uri.AbsoluteUri, out var scope) ? scope : Global;

    /// <summary>
    /// Looks up the scope <paramref name="uri"/> defines, or the scope it is declared in.
    /// </summary>
    /// <returns><c>false</c> when the tree does not know <paramref name="uri"/>.</returns>
    public bool TryGetScope(Uri uri, [NotNullWhen(true)] out LexicalScope? scope)
        => _scopeByUri.TryGetValue(uri.AbsoluteUri, out scope);
}
