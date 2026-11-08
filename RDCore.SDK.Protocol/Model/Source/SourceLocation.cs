namespace RDCore.SDK.Model.Source;

/// <summary>
/// A <c>SourceRange</c> within a specific workspace document.
/// </summary>
/// <param name="Uri">The <c>Uri</c> of the source document.</param>
/// <param name="Range">The range within the source document.</param>
public readonly record struct SourceLocation(Uri Uri, SourceRange Range) : IComparable<SourceLocation>
{
    /// <summary>
    /// Compares two locations by where their ranges start, in document order; the documents are not compared.
    /// </summary>
    /// <param name="other">The location to compare with.</param>
    /// <returns>Less than zero when this location starts before <paramref name="other"/>, zero when they start at the same position, greater than zero when
    /// it starts after.</returns>
    public int CompareTo(SourceLocation other) => Range.Start.CompareTo(other.Range.Start);
}
