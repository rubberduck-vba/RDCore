using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using System.Collections.Immutable;

namespace RDCore.SDK.Semantics;

/// <summary>
/// Represents and encapsulates the immutable context of a <em>semantic operation</em>.
/// </summary>
/// <param name="Diagnostics">The <em>diagnostics</em> associated with this context.</param>
/// <param name="Flags">The <em>semantic flags> associated with this context.</em></param>
public record class SemanticContext<TFlags>(ImmutableArray<Diagnostic> Diagnostics, TFlags Flags) 
    where TFlags : struct, Enum
{
    /// <summary>
    /// Creates an object that represents and encapsulates the immutable context of a <em>semantic operation</em>.
    /// </summary>
    public SemanticContext():this([], (TFlags)(object)0) { }
}
