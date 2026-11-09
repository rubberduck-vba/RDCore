using RDCore.SDK.Model.Source;

namespace RDCore.SDK.Model.Errors.Abstract;

/// <summary>
/// Encapsulates the <em>serializable error data</em> for an <em>error</em>.
/// </summary>
/// <remarks>
/// 🧩 Errors should be used to generate <em>error diagnostics</em>.
/// </remarks>
/// <param name="ErrorId">The numeric (<c>int</c>) representation of the formal error code.</param>
/// <param name="Location">The document location of the faulted CST node.</param>
/// <param name="Description">An optional error description. "Syntax error" unless specified otherwise.</param>
/// <param name="Verbose">
/// A detailed message identifying the faulted CST token and detailing its semantics, as plain text. For a run-time error the runtime recorded
/// <see cref="Details"/> of, it is their <see cref="VBErrorDetails.Message"/>: what a client that formats no details shows.
/// </param>
public abstract record class VBErrorInfo(int ErrorId, SourceLocation Location, string Description, string Verbose)
{
    /// <summary>
    /// What the runtime was doing when it raised the error, as data: the expression, and the frames of the operation that failed. A client formats them its
    /// own way.
    /// </summary>
    /// <remarks>
    /// <see langword="null"/> for an error the runtime recorded no details of: a syntax or compile error, or a run-time error raised where nothing was being
    /// evaluated.
    /// </remarks>
    public VBErrorDetails? Details { get; init; }
}
