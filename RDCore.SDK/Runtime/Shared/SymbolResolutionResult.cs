using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols.Abstract;
using System.Collections.Immutable;

namespace RDCore.SDK.Runtime.Shared;

/// <summary>
/// The result of resolving an <em>identifier name</em> against a scope. Mirrors the other
/// <c>*Result</c> records: it carries a payload (the bound <see cref="Symbol"/>), or a diagnostic,
/// never both.
/// </summary>
/// <remarks>
/// The resolver reports <em>which</em> compile-time error a lookup hit, not a located
/// <see cref="VBCompileErrorInfo"/>: it does not know where the reference is. The caller, which has
/// the syntax node, builds the diagnostic from <see cref="ErrorCode"/> and <see cref="Candidates"/>.
/// The errors a lookup hits:
/// <list type="bullet">
/// <item><em>Duplicate declaration in current scope</em>: the name is declared more than once
///   within one module or procedure.</item>
/// <item><em>Ambiguous name detected</em>: the name resolves in more than one enclosing
///   scope (members promoted from different modules or references); the reference must qualify it.</item>
/// <item><em>Definitions of property procedures for the same property are inconsistent</em>: the name resolves to a
///   property's <c>Get</c>/<c>Let</c>/<c>Set</c> accessors, but their declarations are not a valid
///   property (<strong>MS-VBAL §5.3.1.7</strong>).</item>
/// <item><em>Argument required for Property Let or Property Set</em>: the name resolves to a
///   <c>Property Let</c> or <c>Property Set</c> declared with no parameters at all, so it has no value
///   parameter (<strong>MS-VBAL §5.3.1.5</strong>: <c>value-param</c> is never bracketed).</item>
/// </list>
/// </remarks>
/// <param name="Symbol">The bound symbol, or <c>null</c> when the name is unbound or in error.</param>
/// <param name="ErrorCode">The diagnostic code of the compile-time error the lookup hit (a <see cref="RDCoreDiagnosticCode.CompilePrefix"/> code), or <c>null</c>.</param>
/// <param name="Candidates">The colliding declarations for an error result; empty otherwise.</param>
public readonly record struct SymbolResolutionResult(
    Symbol? Symbol,
    RDCoreDiagnosticCode? ErrorCode,
    ImmutableArray<Symbol> Candidates)
{
    /// <summary>
    /// <c>true</c> when the name bound to exactly one symbol.
    /// </summary>
    public bool IsResolved => Symbol is not null && ErrorCode is null;

    /// <summary>
    /// <c>true</c> when no scope declared the name: not an error in itself.
    /// </summary>
    public bool IsUnbound => Symbol is null && ErrorCode is null;

    /// <summary>
    /// <c>true</c> when the lookup hit a compile-time error.
    /// </summary>
    public bool IsError => ErrorCode is not null;

    /// <summary>
    /// A successful resolution to <paramref name="symbol"/>.
    /// </summary>
    public static SymbolResolutionResult Resolved(Symbol symbol) => new(symbol, null, []);

    /// <summary>
    /// The name is declared nowhere visible from the origin scope.
    /// </summary>
    public static readonly SymbolResolutionResult Unbound = new(null, null, []);
}
