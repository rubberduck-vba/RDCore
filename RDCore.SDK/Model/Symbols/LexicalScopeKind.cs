namespace RDCore.SDK.Model.Symbols;

/// <summary>
/// The tier of a <see cref="LexicalScope"/> in the resolution tree. Distinct from
/// <see cref="Abstract.ScopeKind"/>, which says where a symbol is <em>allocated</em>, not where a
/// name <em>binds</em>. A lookup walks these outward: <see cref="Procedure"/> → <see cref="Module"/>
/// → <see cref="Project"/> → <see cref="Global"/> (<strong>RD-VBAL §2.3.1.2</strong>).
/// </summary>
public enum LexicalScopeKind
{
    /// <summary>
    /// The root scope: precompiler constants, the workspace's module and library names, and the
    /// language's own globals. Every lookup that is not bound sooner ends here.
    /// </summary>
    Global,

    /// <summary>
    /// The workspace project. A standard module's non-<c>Private</c> members are visible here to
    /// every sibling module; a class module's instance members are not.
    /// </summary>
    Project,

    /// <summary>One module body — the members that module declares.</summary>
    Module,

    /// <summary>
    /// One procedure, property, function, or event body — its parameters and its procedure-local
    /// <c>Dim</c> / <c>Static</c> / <c>Const</c> declarations.
    /// </summary>
    Procedure,
}
