namespace RDCore.SDK.Model.Source;

/// <summary>
/// A <c>SourceRange</c> within a specific workspace document.
/// </summary>
/// <param name="Uri">The <c>Uri</c> of the source document.</param>
/// <param name="Range">The range within the source document.</param>
public readonly record struct SourceLocation(Uri Uri, SourceRange Range);
