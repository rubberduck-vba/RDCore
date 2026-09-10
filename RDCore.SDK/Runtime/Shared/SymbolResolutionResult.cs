using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols.Abstract;
using System.Collections.Immutable;

namespace RDCore.SDK.Runtime.Shared;

/// <summary>
/// The result of resolving an <em>identifier name</em> against a scope. Mirrors the other
/// <c>*Result</c> records: it carries a payload — the bound <see cref="Symbol"/> — or a diagnostic,
/// never both.
/// </summary>
/// <remarks>
/// The resolver reports <em>which</em> compile-time error a lookup hit, not a located
/// <see cref="VBCompileErrorInfo"/> — it does not know where the reference is. The caller, which has
/// the syntax node, builds the diagnostic from <see cref="ErrorId"/> and <see cref="Candidates"/>.
/// <list type="bullet">
/// <item><see cref="VBCompileErrorId.DuplicateDeclaration"/> — the name is declared more than once
///   within one module or procedure.</item>
/// <item><see cref="VBCompileErrorId.AmbiguousName"/> — the name resolves in more than one enclosing
///   scope (members promoted from different modules or references); the reference must qualify it.</item>
/// </list>
/// </remarks>
/// <param name="Symbol">The bound symbol, or <c>null</c> when the name is unbound or in error.</param>
/// <param name="ErrorId">The compile-time error the lookup hit, or <c>null</c>.</param>
/// <param name="Candidates">The colliding declarations for an error result; empty otherwise.</param>
public readonly record struct SymbolResolutionResult(
    Symbol? Symbol,
    VBCompileErrorId? ErrorId,
    ImmutableArray<Symbol> Candidates)
{
    /// <summary><c>true</c> when the name bound to exactly one symbol.</summary>
    public bool IsResolved => Symbol is not null && ErrorId is null;

    /// <summary><c>true</c> when no scope declared the name — not an error in itself.</summary>
    public bool IsUnbound => Symbol is null && ErrorId is null;

    /// <summary><c>true</c> when the lookup hit a compile-time error.</summary>
    public bool IsError => ErrorId is not null;

    /// <summary>A successful resolution to <paramref name="symbol"/>.</summary>
    public static SymbolResolutionResult Resolved(Symbol symbol) => new(symbol, null, []);

    /// <summary>The name is declared nowhere visible from the origin scope.</summary>
    public static readonly SymbolResolutionResult Unbound = new(null, null, []);

    /// <summary>
    /// The name is declared more than once in a single scope
    /// (<see cref="VBCompileErrorId.DuplicateDeclaration"/>).
    /// </summary>
    public static SymbolResolutionResult Duplicate(IEnumerable<Symbol> candidates)
        => new(null, VBCompileErrorId.DuplicateDeclaration, [.. candidates]);

    /// <summary>
    /// The name resolves in more than one enclosing scope and must be qualified
    /// (<see cref="VBCompileErrorId.AmbiguousName"/>).
    /// </summary>
    public static SymbolResolutionResult Ambiguous(IEnumerable<Symbol> candidates)
        => new(null, VBCompileErrorId.AmbiguousName, [.. candidates]);
}
