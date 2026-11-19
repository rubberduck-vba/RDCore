using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace RDCore.Runtime.Semantics.Precompiler;

/// <summary>
/// The names of the conditional-compilation constants a session's project defines (a <c>.rdproj</c> <c>#Const</c>), as the session was composed with them.
/// </summary>
/// <remarks>
/// The session's constants are one set, whichever defined them: what a <c>#If</c> means on another platform
/// (<see cref="PrecompilerLiveBranchEvaluator.CompiledPlatformsOf"/>) takes the value the project gives a predefined constant on every platform, and that
/// of the platform otherwise, which the set alone does not say.
/// </remarks>
public static class ProjectConditionalConstants
{
    private static readonly ConditionalWeakTable<IRuntimeSession, IReadOnlySet<string>> Defined = new();

    private static readonly IReadOnlySet<string> None = new HashSet<string>();

    /// <summary>
    /// Records the names of the constants the project of <paramref name="session"/> defines.
    /// </summary>
    /// <param name="session">A session, as it is composed.</param>
    /// <param name="names">The names of the project's constants.</param>
    public static void Set(IRuntimeSession session, IEnumerable<string> names)
        => Defined.AddOrUpdate(session, new HashSet<string>(names, StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// The names of the constants the project of <paramref name="session"/> defines: none for a session composed without a project.
    /// </summary>
    /// <param name="session">A session.</param>
    /// <returns>The names, compared without regard to case.</returns>
    public static IReadOnlySet<string> Of(IRuntimeSession session) => Defined.TryGetValue(session, out var names) ? names : None;

    /// <summary>
    /// The constants the project of <paramref name="session"/> defines, each with the value the session binds it to, in the order of their names.
    /// </summary>
    /// <param name="session">A session.</param>
    /// <returns>The facts the host states of them to the analyzers; none for a session composed without a project.</returns>
    public static ImmutableArray<ConditionalConstantFact> FactsOf(IRuntimeSession session)
        =>
        [
            .. Of(session).Order(StringComparer.OrdinalIgnoreCase)
                .Select(name => session.Symbols.Resolver.ResolveConditionalConstant(name, ScopeKind.Global, StaticSymbol.GlobalUri).Symbol)
                .OfType<PrecompilerConstantSymbol>()
                .Select(constant => new ConditionalConstantFact(constant.Name, constant.Value)),
        ];
}
