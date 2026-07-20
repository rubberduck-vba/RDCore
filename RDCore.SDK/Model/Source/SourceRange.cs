namespace RDCore.SDK.Model.Source;

/// <summary>
/// A contiguous span in a source document, from a <c>Start</c> position (inclusive) to an <c>End</c> position (exclusive).
/// </summary>
/// <param name="Start">The inclusive start position of the range.</param>
/// <param name="End">The exclusive end position of the range.</param>
public readonly record struct SourceRange(SourcePosition Start, SourcePosition End)
{
    /// <summary>
    /// Creates a <c>SourceRange</c> from zero-based start/end line numbers and character offsets.
    /// </summary>
    public SourceRange(int startLine, int startCharacter, int endLine, int endCharacter)
        : this(new SourcePosition(startLine, startCharacter), new SourcePosition(endLine, endCharacter)) { }

    /// <summary>
    /// <c>true</c> if this range spans no characters (<c>Start</c> and <c>End</c> are the same position).
    /// </summary>
    public bool IsEmpty => Start == End;
}
