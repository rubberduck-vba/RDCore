using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using System.Collections.Concurrent;

namespace RDCore.SDK.Semantics;

/// <summary>
/// Builds an immutable <c>SemanticContext</c> encapsulating the context of a <em>semantic operation</em>.
/// </summary>
/// <remarks>
/// ⚠️ Like this base implementation, any inherited builder types should use <c>ConcurrentBag&lt;T&gt;</c> to accumulate the semantic state in a <em>thread-safe</em> manner.
/// </remarks>
/// <typeparam name="TContext">The type of <c>SemanticContext</c> to build.</typeparam>
/// <typeparam name="TFlags">A <c>[Flags]</c> <c>enum</c> type with bit-shifted members that can be composed to encode the <em>semantic flags</em> of the context.</typeparam>
public record class SemanticContextBuilder<TContext, TFlags> 
    where TContext : SemanticContext<TFlags>, new()
    where TFlags : struct, Enum
{
    private readonly ConcurrentBag<Diagnostic> _diagnostics = [];
    private readonly ConcurrentBag<TFlags> _flags = [];

    /// <summary>
    /// Adds the specified <em>diagnostic</em> to the context.
    /// </summary>
    /// <param name="diagnostic"></param>
    public void WithDiagnostic(Diagnostic diagnostic) => _diagnostics.Add(diagnostic);
    /// <summary>
    /// Adds the specified <em>semantic flag(s)</em> to the context.
    /// </summary>
    /// <param name="flags">The semantic flag values.</param>
    public void WithFlags(TFlags flags) => _flags.Add(flags);

    /// <summary>
    /// Builds and returns an immutable <c>SemanticContext</c> instance from the current builder state.
    /// </summary>
    public virtual TContext Build() => new TContext() with 
    { 
        Diagnostics = [.. _diagnostics],
        Flags = (TFlags)(object)_flags.Cast<object>().Cast<int>().Aggregate((current, value) => current | value)
    };
}