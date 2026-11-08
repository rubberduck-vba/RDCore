using RDCore.SDK.Model.Errors.Abstract;
using System.Collections.Immutable;

namespace RDCore.SDK.Semantics.Context.Abstract;

/// <summary>
/// Represents and encapsulates the immutable context of a <em>semantic operation</em>.
/// </summary>
public record class SemanticContext<TFlags>
    where TFlags : struct, Enum
{
    /// <summary>
    /// Creates an object that represents and encapsulates the immutable context of a <em>semantic operation</em>.
    /// </summary>
    public SemanticContext() : this(default, []) { }

    /// <summary>
    /// Creates an object that represents and encapsulates the immutable context of a <em>semantic operation</em>.
    /// </summary>
    /// <param name="errors">The <em>error info</em> metadata associated with this context.</param>
    /// <param name="flags">The <em>semantic flags> associated with this context.</em></param>
    /// <remarks>This overload should only be used in the <em>language core</em>.</remarks>
    internal SemanticContext(TFlags flags, ImmutableArray<VBErrorInfo> errors)
    {
        Errors = errors;
        Flags = flags;
    }

    public ImmutableArray<VBErrorInfo> Errors { get; init; }
    /// <summary>
    /// Gets a <c>TFlags</c> (enum) value representing the semantic flags associated with this context.
    /// </summary>
    public TFlags Flags { get; init; }
}
