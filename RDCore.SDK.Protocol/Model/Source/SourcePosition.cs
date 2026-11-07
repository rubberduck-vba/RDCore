namespace RDCore.SDK.Model.Source;

/// <summary>
/// A position in a source document, expressed as a zero-based line number and a zero-based character offset.
/// </summary>
/// <remarks>
/// 👉 Character offsets count <em>UTF-16 code units</em>, consistent with both the LSP default position encoding
/// and the native encoding of VBA (BSTR) strings.
/// </remarks>
/// <param name="Line">The zero-based line number.</param>
/// <param name="Character">The zero-based character offset within the line.</param>
public readonly record struct SourcePosition(int Line, int Character) : 
    IComparable<SourcePosition>, 
    IEquatable<SourcePosition>
{
    /// <summary>
    /// Position L0C0.
    /// </summary>
    public static SourcePosition Zero { get; } = new(0, 0);

    /// <summary>
    /// Translates this position, which is relative to a fragment of a document, into a position in that
    /// document, where <paramref name="anchor"/> is where the fragment itself begins in it.
    /// </summary>
    /// <param name="anchor">The position, in the document, of the fragment's first character.</param>
    /// <returns>The same point in the source, expressed in the document's own coordinates.</returns>
    /// <remarks>
    /// 👉 This is <em>not</em> a vector addition, and <c>operator +</c> is not a substitute for it. The
    /// anchor's <see cref="Character"/> applies only to the fragment's first line, because that is the
    /// only line the fragment shares with what precedes it: every later line of the fragment starts at
    /// the document's column 0 just as it starts at the fragment's. Adding the anchor's character offset
    /// to every line instead pushes each line after the first that many columns to the right.
    /// </remarks>
    public SourcePosition AnchoredAt(SourcePosition anchor) => new(
        anchor.Line + Line,
        Line == 0 ? anchor.Character + Character : Character);

    /// <summary>
    /// Compares two positions in document order: by line, then by character within the line.
    /// </summary>
    /// <param name="other">The position to compare with.</param>
    /// <returns>Less than zero when this position comes before <paramref name="other"/>, zero when they are the same, greater than zero when it comes after.</returns>
    public int CompareTo(SourcePosition other) =>
        Line != other.Line ? Line.CompareTo(other.Line) : Character.CompareTo(other.Character);

    /// <summary>Whether <paramref name="left"/> comes before <paramref name="right"/> in the document.</summary>
    public static bool operator <(SourcePosition left, SourcePosition right) => left.CompareTo(right) < 0;

    /// <summary>Whether <paramref name="left"/> comes after <paramref name="right"/> in the document.</summary>
    public static bool operator >(SourcePosition left, SourcePosition right) => left.CompareTo(right) > 0;

    /// <summary>Whether <paramref name="left"/> comes before <paramref name="right"/> in the document, or is the same position.</summary>
    public static bool operator <=(SourcePosition left, SourcePosition right) => left.CompareTo(right) <= 0;

    /// <summary>Whether <paramref name="left"/> comes after <paramref name="right"/> in the document, or is the same position.</summary>
    public static bool operator >=(SourcePosition left, SourcePosition right) => left.CompareTo(right) >= 0;

    /// <summary>
    /// The component-wise sum of two positions: lines added to lines, characters to characters.
    /// </summary>
    /// <remarks>👉 Not a translation into a document's coordinates: <see cref="AnchoredAt"/> is.</remarks>
    public static SourcePosition operator +(SourcePosition left, SourcePosition right) => new(left.Line + right.Line, left.Character + right.Character);

    /// <summary>
    /// The component-wise difference of two positions: lines from lines, characters from characters.
    /// </summary>
    public static SourcePosition operator -(SourcePosition left, SourcePosition right) => new(left.Line - right.Line, left.Character - right.Character);

    /// <summary>
    /// A string representation of this source position.
    /// </summary>
    /// <remarks>
    /// For internal/debugging purposes only: this representation is not intended to be surfaced.
    /// </remarks>
    /// <returns>Line/Character position in a <c>L0C0</c> notation</returns>
    public override string ToString() => $"L{Line}C{Character}";

    /// <summary>Whether two positions are the same line and the same character.</summary>
    /// <param name="other">The position to compare with.</param>
    public bool Equals(SourcePosition other) => other.Line == Line && other.Character == Character;

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Line, Character);
}
