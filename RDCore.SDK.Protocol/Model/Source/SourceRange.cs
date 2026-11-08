namespace RDCore.SDK.Model.Source;

/// <summary>
/// A contiguous span in a source document, from a <c>Start</c> position (inclusive) to an <c>End</c> position (exclusive).
/// </summary>
/// <param name="Start">The inclusive start position of the range.</param>
/// <param name="End">The exclusive end position of the range.</param>
/// <remarks>
/// <c>IComparable</c> compares source location start.
/// </remarks>
public readonly record struct SourceRange(SourcePosition Start, SourcePosition End) : IComparable<SourceRange>
{
    /// <summary>
    /// Gets an empty range at position <c>L0C0</c>.
    /// </summary>
    public static SourceRange Empty { get; } = new(SourcePosition.Zero, SourcePosition.Zero);

    /// <summary>
    /// Creates a <c>SourceRange</c> from zero-based start/end line numbers and character offsets.
    /// </summary>
    public SourceRange(int startLine, int startCharacter, int endLine, int endCharacter)
        : this(new SourcePosition(startLine, startCharacter), new SourcePosition(endLine, endCharacter)) { }

    /// <summary>
    /// <c>true</c> if this range spans no characters (<c>Start</c> and <c>End</c> are the same position).
    /// </summary>
    public bool IsEmpty => Start == End;

    /// <summary>
    /// Compares two ranges by where they start, in document order.
    /// </summary>
    /// <param name="other">The range to compare with.</param>
    /// <returns>Less than zero when this range starts before <paramref name="other"/>, zero when they start at the same position, greater than zero when it
    /// starts after.</returns>
    public int CompareTo(SourceRange other) => Start.CompareTo(other.Start);
}
