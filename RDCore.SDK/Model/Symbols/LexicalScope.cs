using RDCore.SDK.Model.Symbols.Abstract;

namespace RDCore.SDK.Model.Symbols;

/// <summary>
/// One lexical scope of a composed workspace — a compile-time region a name lookup starts from and
/// then walks outward, through the <see cref="Parent"/> chain, until the name binds or the global
/// scope is reached (<strong>MS-VBAL §5.2</strong> Name Binding, <strong>RD-VBAL §2.3.1.2</strong>).
/// </summary>
/// <remarks>
/// A lexical scope is not a <see cref="ScopeKind"/> — that says which allocation heap a symbol lives
/// in — nor a run-time call-stack frame, which holds one activation's values. The tree has four
/// tiers: the global scope, the project scope (a standard module's non-<c>Private</c> members,
/// visible to every sibling module), one scope per module, and one per procedure / property /
/// function / event body. Ordering referenced libraries by their <c>.rdproj</c> priority within the
/// global scope is not modelled yet.
/// </remarks>
public sealed class LexicalScope
{
    private readonly ILookup<string, Symbol> _declarations;

    /// <summary>
    /// Creates a scope belonging to the symbol at <paramref name="uri"/> that directly declares
    /// <paramref name="declarations"/>.
    /// </summary>
    /// <param name="uri">
    /// The <see cref="Symbol.Uri"/> of the symbol this scope belongs to — a module, a procedure, the
    /// workspace root for the project scope, or the well-known <see cref="StaticSymbol.GlobalUri"/>
    /// for the global scope.
    /// </param>
    /// <param name="kind">Which tier of the resolution tree this scope is.</param>
    /// <param name="parent">The enclosing scope, or <c>null</c> for the global scope.</param>
    /// <param name="declarations">
    /// The symbols declared <em>directly</em> in this scope. A name declared more than once here
    /// (a field and a procedure that collide, two <c>Dim</c>s of one name) keeps every declaration —
    /// reporting the <em>ambiguous name</em> is the caller's concern.
    /// </param>
    /// <param name="directives">
    /// The declaring module's <see cref="ModuleDirectives"/> — <c>null</c> for every tier except the
    /// module scope itself, which carries its <see cref="VBModuleSymbol.Directives"/>.
    /// </param>
    public LexicalScope(Uri uri, LexicalScopeKind kind, LexicalScope? parent, IEnumerable<Symbol> declarations, ModuleDirectives? directives = null)
    {
        Uri = uri;
        Kind = kind;
        Parent = parent;
        Directives = directives;
        _declarations = declarations.ToLookup(symbol => symbol.Name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The <see cref="Symbol.Uri"/> of the symbol this scope belongs to.</summary>
    public Uri Uri { get; }

    /// <summary>Which tier of the resolution tree this scope is.</summary>
    public LexicalScopeKind Kind { get; }

    /// <summary>The enclosing scope, or <c>null</c> for the global scope.</summary>
    public LexicalScope? Parent { get; }

    /// <summary>The declaring module's directives, for the module scope itself; <c>null</c> elsewhere.</summary>
    public ModuleDirectives? Directives { get; }

    /// <summary>
    /// The <see cref="ModuleDirectives"/> of the module enclosing this scope — itself, if this
    /// <em>is</em> the module scope, otherwise the nearest ancestor that carries them. <c>null</c>
    /// when no enclosing module scope exists (the global or project scope, reached directly).
    /// </summary>
    public ModuleDirectives? EnclosingModuleDirectives() => SelfAndAncestors().Select(scope => scope.Directives).FirstOrDefault(directives => directives is not null);

    /// <summary>
    /// The symbols this scope declares directly under <paramref name="name"/>, matched
    /// case-insensitively (<strong>MS-VBAL §3.3.5</strong> — identifiers are not case-sensitive).
    /// Empty when the name is not declared here; more than one element means the name is ambiguous
    /// within this scope.
    /// </summary>
    public IEnumerable<Symbol> DeclaredAs(string name) => _declarations[name];

    /// <summary>
    /// This scope, then each enclosing scope out to the global scope — the order a name lookup
    /// visits them (<strong>RD-VBAL §2.3.1.2</strong>).
    /// </summary>
    public IEnumerable<LexicalScope> SelfAndAncestors()
    {
        for (var scope = this; scope is not null; scope = scope.Parent)
        {
            yield return scope;
        }
    }
}
