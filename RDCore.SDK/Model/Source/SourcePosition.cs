namespace RDCore.SDK.Model.Source;

/// <summary>
/// A position in a source document, expressed as a zero-based line number and a zero-based character offset.
/// </summary>
/// <remarks>
/// 👉 Character offsets count <em>UTF-16 code units</em>, consistent with both the LSP default position encoding
/// and the native encoding of VBA (BSTR) strings.
/// </remarks>
/// <param name="Line">The zero-based line number.</param>
/// <param name="Character">The zero-based character (UTF-16 code unit) offset within the line.</param>
public readonly record struct SourcePosition(int Line, int Character) : IComparable<SourcePosition>
{
    public int CompareTo(SourcePosition other) =>
        Line != other.Line ? Line.CompareTo(other.Line) : Character.CompareTo(other.Character);

    public static bool operator <(SourcePosition left, SourcePosition right) => left.CompareTo(right) < 0;
    public static bool operator >(SourcePosition left, SourcePosition right) => left.CompareTo(right) > 0;
    public static bool operator <=(SourcePosition left, SourcePosition right) => left.CompareTo(right) <= 0;
    public static bool operator >=(SourcePosition left, SourcePosition right) => left.CompareTo(right) >= 0;
}
