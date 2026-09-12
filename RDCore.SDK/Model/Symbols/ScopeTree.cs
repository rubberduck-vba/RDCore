using RDCore.SDK.Model.Symbols.Abstract;
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
