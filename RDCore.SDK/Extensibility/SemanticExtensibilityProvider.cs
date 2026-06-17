using RDCore.SDK.Semantics.Context.Abstract;

namespace RDCore.SDK.Extensibility;

/// <summary>
/// Signals a starting, ongoing, completed, succeeded, or failed semantic operation to an extension process via LSP.
/// </summary>
/// <typeparam name="TContext">The type of the <c>SemanticContext</c> to be externally enriched.</typeparam>
/// <typeparam name="TFlags">The type of <em>semantic flags</em> associated with this context.</typeparam>
public interface ISemanticExtensibilityProvider<TContext, TFlags> 
    where TContext : SemanticContext<TFlags>
    where TFlags : struct, Enum
{
    /// <summary>
    /// Serializes and broadcasts the semantic context over LSP to any interested semantic analyzer extensions.
    /// </summary>
    /// <param name="context">The semantic context.</param>
    /// <returns>An asynchronous <c>Task</c> that completes when the semantic context is transmitted over LSP to all interested extensions.</returns>
    Task OnSemanticOperationAsync(TContext context);
}

internal class SemanticExtensibilityProvider<TContext, TFlags>() : ISemanticExtensibilityProvider<TContext, TFlags>
    where TContext : SemanticContext<TFlags>
    where TFlags : struct, Enum
{
    public async Task OnSemanticOperationAsync(TContext context)
    {
        // TODO make this happen
    }
}
