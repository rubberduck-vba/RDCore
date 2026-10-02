using TextDocumentRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.LanguageServer.Workspace;

/// <summary>
/// Applies the edits a client sends to the text of a document (<strong>LSP 3.17</strong> <c>textDocument/didChange</c>).
/// </summary>
/// <remarks>
/// A position is a zero-based line and a zero-based character, in UTF-16 code units: the unit of a .NET <see cref="string"/>, so an offset into the text is a
/// character offset and no encoding is involved. A line ends with <c>\r\n</c>, <c>\n</c> or <c>\r</c>, whichever the document uses, and an edit leaves every
/// other line ending as it was. As the specification says, a character that is past the end of its line is the end of the line, and a line that is past the end of
/// the document is the end of the document.
/// </remarks>
internal static class DocumentText
{
    /// <summary>
    /// Replaces what a range covers with <paramref name="newText"/>.
    /// </summary>
    /// <param name="text">The text of the document.</param>
    /// <param name="range">The range to replace.</param>
    /// <param name="newText">What replaces it.</param>
    /// <returns>The text with the edit applied.</returns>
    public static string Apply(string text, TextDocumentRange range, string newText)
    {
        var start = OffsetOf(text, range.Start.Line, range.Start.Character);
        var end = Math.Max(start, OffsetOf(text, range.End.Line, range.End.Character));
        return string.Concat(text.AsSpan(0, start), newText, text.AsSpan(end));
    }

    /// <summary>
    /// The offset into <paramref name="text"/> of a position.
    /// </summary>
    /// <param name="text">The text of the document.</param>
    /// <param name="line">The zero-based line.</param>
    /// <param name="character">The zero-based character on the line, in UTF-16 code units.</param>
    public static int OffsetOf(string text, int line, int character)
    {
        var lineStart = 0;
        for (var current = 0; current < line; current++)
        {
            var next = EndOfLine(text, lineStart);
            if (next >= text.Length)
            {
                // a line that is past the end of the document is the end of the document.
                return text.Length;
            }

            lineStart = next + (text[next] == '\r' && next + 1 < text.Length && text[next + 1] == '\n' ? 2 : 1);
        }

        // a character that is past the end of its line is the end of the line.
        return Math.Min(lineStart + Math.Max(character, 0), EndOfLine(text, lineStart));
    }

    // the offset of the terminator of the line that starts at lineStart, or of the end of the text.
    private static int EndOfLine(string text, int lineStart)
    {
        var index = text.AsSpan(lineStart).IndexOfAny('\r', '\n');
        return index < 0 ? text.Length : lineStart + index;
    }
}
